using System.Xml.Linq;

namespace Callrift.Core;

internal sealed class ProjectClassifier(SourceSnapshot snapshot)
{
    private readonly IReadOnlyList<(string Path, string Directory, Classification Kind)> projects = snapshot.Files
        .Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        .Select(f => (Path: f.Path, Directory: DirectoryOf(f.Path), Kind: ClassifyProject(f, snapshot)))
        .OrderByDescending(p => p.Directory.Length).ToArray();

    public bool IsTest(string path)
    {
        foreach (var project in projects)
            if (project.Directory.Length == 0 || path.StartsWith(project.Directory + "/", StringComparison.Ordinal))
                return project.Kind.IsTest ?? IsTestPath(path);
        return IsTestPath(path);
    }

    public IReadOnlyList<AnalysisDiagnostic> Diagnostics => projects.Where(p => p.Kind.Uncertain || p.Kind.IsTest is null && IsTestPath(p.Directory))
        .Select(p => new AnalysisDiagnostic("test-project-inferred",
            $"Test classification for {p.Path} uses literal metadata and directory naming; use MSBuild mode to evaluate imported or conditional metadata.", new SourceLocation(p.Path, 1, 1)))
        .OrderBy(d => d.Location!.Path, StringComparer.Ordinal).ToArray();

    private static bool IsTestPath(string path) => path.Split('/').Any(segment => segment.Equals("tests", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("test", StringComparison.OrdinalIgnoreCase)
            || segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));

    private static Classification ClassifyProject(SourceFile file, SourceSnapshot snapshot)
    {
        var candidates = new List<SourceFile> { file };
        var directory = DirectoryOf(file.Path);
        candidates.AddRange(snapshot.Files.Where(f => f.Path.EndsWith("Directory.Build.props", StringComparison.Ordinal)
            && (DirectoryOf(f.Path).Length == 0 || (directory + "/").StartsWith(DirectoryOf(f.Path) + "/", StringComparison.Ordinal)))
            .OrderByDescending(f => DirectoryOf(f.Path).Length).Take(1));
        var documents = new List<XDocument>();
        var uncertain = false;
        foreach (var candidate in candidates)
        {
            try
            {
                documents.Add(XDocument.Parse(candidate.Content));
            }
            catch (System.Xml.XmlException) { uncertain = true; }
        }
        foreach (var document in documents)
        {
            var property = document.Descendants().LastOrDefault(e => e.Name.LocalName == "IsTestProject");
            if (property is null) continue;
            if (IsUnconditional(property) && bool.TryParse(property.Value.Trim(), out var isTest)) return new Classification(isTest, uncertain);
            uncertain = true;
            break;
        }
        var testReferences = documents.SelectMany(d => d.Descendants()).Where(IsTestReference).ToArray();
        if (testReferences.Any(IsUnconditional)) return new Classification(true, uncertain);
        uncertain |= testReferences.Length > 0;
        var projectType = documents.SelectMany(d => d.Descendants().Where(e => e.Name.LocalName == "ProjectType").Reverse()).FirstOrDefault();
        if (projectType is not null && projectType.Value.Trim().Equals("Test", StringComparison.OrdinalIgnoreCase))
            return new Classification(IsUnconditional(projectType) ? true : null, true);
        var outputType = documents.SelectMany(d => d.Descendants().Where(e => e.Name.LocalName == "OutputType").Reverse()).FirstOrDefault();
        if (outputType is not null)
        {
            if (!IsUnconditional(outputType) || outputType.Value.Contains("$(", StringComparison.Ordinal)) return new Classification(null, true);
            if (outputType.Value.Trim().Equals("Exe", StringComparison.OrdinalIgnoreCase) || outputType.Value.Trim().Equals("WinExe", StringComparison.OrdinalIgnoreCase)) return new Classification(false, uncertain);
        }
        if (documents.Any(d => ((string?)d.Root?.Attribute("Sdk"))?.Split(';').Any(s => s.Trim().Equals("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)) == true)) return new Classification(false, uncertain);
        return new Classification(null, uncertain);
    }

    private static bool IsUnconditional(XElement element) => element.AncestorsAndSelf().All(e => string.IsNullOrWhiteSpace((string?)e.Attribute("Condition")));

    private static bool IsTestReference(XElement element)
    {
        var name = ((string?)element.Attribute("Include"))?.Trim().ToLowerInvariant();
        return element.Name.LocalName switch
        {
            "PackageReference" => name is "xunit" or "xunit.v3"
                or "xunit.v3.mtp-v1" or "xunit.v3.mtp-v2" or "xunit.v3.mtp-off"
                or "xunit.v3.aot" or "xunit.v3.aot.mtp-v2" or "xunit.v3.aot.mtp-off"
                or "nunit" or "mstest.testframework" or "microsoft.net.test.sdk",
            "Reference" => name?.Split(',')[0].Trim() is "mstest.testframework" or "microsoft.visualstudio.testplatform.testframework"
                or "nunit.framework" or "xunit.core" or "xunit.v3.core",
            _ => false
        };
    }

    private static string DirectoryOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    private sealed record Classification(bool? IsTest, bool Uncertain);
}
