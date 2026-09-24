using System.Xml.Linq;

namespace Callrift.Core;

internal sealed class ProjectClassifier(SourceSnapshot snapshot)
{
    private readonly IReadOnlyList<(string Directory, bool IsTest)> projects = snapshot.Files
        .Where(f => f.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        .Select(f => (Directory: DirectoryOf(f.Path), IsTest: IsTestProject(f, snapshot)))
        .OrderByDescending(p => p.Directory.Length).ToArray();

    public bool IsTest(string path)
    {
        foreach (var project in projects)
            if (project.Directory.Length == 0 || path.StartsWith(project.Directory + "/", StringComparison.Ordinal))
                return project.IsTest;
        return path.Split('/').Any(segment => segment.Equals("tests", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("test", StringComparison.OrdinalIgnoreCase)
            || segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTestProject(SourceFile file, SourceSnapshot snapshot)
    {
        var candidates = new List<SourceFile> { file };
        var directory = DirectoryOf(file.Path);
        candidates.AddRange(snapshot.Files.Where(f => f.Path.EndsWith("Directory.Build.props", StringComparison.Ordinal)
            && (DirectoryOf(f.Path).Length == 0 || (directory + "/").StartsWith(DirectoryOf(f.Path) + "/", StringComparison.Ordinal))));
        foreach (var candidate in candidates)
        {
            try
            {
                var document = XDocument.Parse(candidate.Content);
                if (document.Descendants().Any(e => e.Name.LocalName == "IsTestProject" && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (document.Descendants().Where(e => e.Name.LocalName == "PackageReference").Any(e =>
                    ((string?)e.Attribute("Include"))?.ToLowerInvariant() is "xunit" or "xunit.v3" or "nunit" or "mstest.testframework" or "microsoft.net.test.sdk"))
                    return true;
            }
            catch (System.Xml.XmlException) { }
        }
        return false;
    }

    private static string DirectoryOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
}
