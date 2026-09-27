namespace Callrift.Scenarios;

internal static class FixtureDirectory
{
    public static string TemporaryRoot => ResolveDirectory(new DirectoryInfo(Path.GetTempPath()));

    public static string CreatePath(string prefix) =>
        Path.Combine(TemporaryRoot, prefix + Guid.NewGuid().ToString("N"));

    private static string ResolveDirectory(DirectoryInfo directory)
    {
        if (directory.Parent is not { } parent) return directory.FullName;
        if (directory.ResolveLinkTarget(true) is { } target)
            return ResolveDirectory(new DirectoryInfo(target.FullName));
        return Path.Combine(ResolveDirectory(parent), directory.Name);
    }
}
