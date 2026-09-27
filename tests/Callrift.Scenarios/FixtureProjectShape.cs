using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Callrift.MSBuild;

namespace Callrift.Scenarios;

internal sealed record FixtureProjectShape(string Key, bool ReuseRoot)
{
    private static readonly HashSet<string> Properties = new(StringComparer.Ordinal)
    {
        "TargetFramework", "LangVersion", "Nullable", "ImplicitUsings", "AllowUnsafeBlocks",
        "DefineConstants", "EnablePreviewFeatures", "CheckForOverflowUnderflow", "OutputType",
        "AssemblyName", "RootNamespace"
    };

    public static FixtureProjectShape? Create(IReadOnlyDictionary<string, string> files, MSBuildOptions options)
    {
        var temporaryRoot = FixtureDirectory.TemporaryRoot;
        for (var ancestor = new DirectoryInfo(temporaryRoot); ancestor is not null; ancestor = ancestor.Parent)
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Directory.Build.rsp" })
                if (File.Exists(Path.Combine(ancestor.FullName, name))) return null;
        var projects = files.Where(file => file.Key.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (projects.Length == 0 || !files.ContainsKey(options.Target)) return null;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!paths.Add(file.Key.Replace('\\', '/'))) return null;
            if (Path.IsPathRooted(file.Key) || file.Key.Replace('\\', '/').Split('/').Any(part => part is "" or "." or "..")) return null;
            if (!file.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !file.Key.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return null;
        }
        try
        {
            foreach (var project in projects)
            {
                var root = XDocument.Parse(project.Value).Root;
                if (root is null || root.Name != "Project" || root.Attributes().Count() != 1 || (string?)root.Attribute("Sdk") != "Microsoft.NET.Sdk") return null;
                foreach (var group in root.Elements())
                {
                    if (group.HasAttributes) return null;
                    if (group.Name == "PropertyGroup")
                    {
                        foreach (var property in group.Elements())
                            if (property.Name.Namespace != XNamespace.None || !Properties.Contains(property.Name.LocalName)
                                || property.HasAttributes || property.HasElements || HasExpression(property.Value)) return null;
                    }
                    else if (group.Name == "ItemGroup")
                    {
                        foreach (var item in group.Elements())
                        {
                            if (item.HasElements || item.Value.Trim().Length != 0 || item.Attributes().Count() != 1) return null;
                            if (item.Name == "Compile" && item.Attribute("Remove") is { } remove && !HasExpression(remove.Value)) continue;
                            if (item.Name != "ProjectReference" || item.Attribute("Include") is not { } reference
                                || HasExpression(reference.Value) || Path.IsPathRooted(reference.Value)) return null;
                            var validationRoot = Path.Combine(temporaryRoot, "callrift-project-shape");
                            var referenced = Path.GetRelativePath(validationRoot, Path.GetFullPath(reference.Value,
                                Path.Combine(validationRoot, Path.GetDirectoryName(project.Key)!))).Replace('\\', '/');
                            if (!projects.Any(candidate => candidate.Key.Replace('\\', '/') == referenced)) return null;
                        }
                    }
                    else return null;
                }
            }
        }
        catch (Exception error) when (error is XmlException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        var inputs = files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => new
        {
            Path = file.Key,
            Content = file.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? "" : file.Value.Replace("\r\n", "\n", StringComparison.Ordinal)
        });
        return new FixtureProjectShape(JsonSerializer.Serialize(new { Options = options, Files = inputs }), projects.Length == 1);
    }

    private static bool HasExpression(string value) => value.IndexOfAny(['$', '@', '%']) >= 0;
}
