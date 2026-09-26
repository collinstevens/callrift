namespace Callrift.Scenarios;

internal static class FixtureDirectory
{
    public static string CreatePath(string prefix) =>
        Path.Combine(ResolveDirectory(new DirectoryInfo(Path.GetTempPath())), prefix + Guid.NewGuid().ToString("N"));

    private static string ResolveDirectory(DirectoryInfo directory)
    {
        if (directory.ResolveLinkTarget(true) is { } target)
            return ResolveDirectory(new DirectoryInfo(target.FullName));
        return directory.Parent is { } parent
            ? Path.Combine(ResolveDirectory(parent), directory.Name)
            : directory.FullName;
    }
}
