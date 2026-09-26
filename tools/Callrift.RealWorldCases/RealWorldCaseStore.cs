using System.Text.Json;
using Callrift.Core;

namespace Callrift.RealWorldCases;

public sealed record RealWorldCase(string Id, string Repository, string CacheName, string License, string LicenseFile,
    string Before, string After, string Reason, string[] Tags, string[] Options, string? KnownIssue,
    RealWorldCaseView[]? Views = null, string? Review = null, string? BeforeLicenseBlob = null, string? AfterLicenseBlob = null, bool Routine = true);

public sealed record RealWorldCaseView(string Id, string[] Options, bool Routine = true);

public static class RealWorldCaseStore
{
    public static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "real-world-cases", "manifest.json"))) return directory.FullName;
        throw new InvalidOperationException("Cannot find real-world-cases/manifest.json.");
    }

    public static IReadOnlyList<RealWorldCase> ReadManifest() => JsonSerializer.Deserialize<RealWorldCase[]>(File.ReadAllText(Path.Combine(FindRoot(), "real-world-cases", "manifest.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static async Task<string> PrepareAsync(RealWorldCase entry, CancellationToken cancellationToken = default)
    {
        var cache = Environment.GetEnvironmentVariable("CALLRIFT_CASES_CACHE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Callrift", "real-world-cases");
        Directory.CreateDirectory(cache);
        if (entry.CacheName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || entry.CacheName.Contains('/') || entry.CacheName.Contains('\\') || entry.CacheName is "." or "..")
            throw new InvalidOperationException("Invalid case cache name.");
        var path = Path.Combine(cache, entry.CacheName);
        using var lease = await AcquirePreparationAsync(path + ".prepare.lock", cancellationToken);
        if (!Directory.Exists(path))
        {
            var staging = Path.GetFullPath(path + ".clone-" + Guid.NewGuid().ToString("N"));
            try
            {
                await GitRepository.RunAsync(cache, ["clone", "--filter=blob:none", "--no-checkout", entry.Repository, staging], cancellationToken);
                Directory.Move(staging, path);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(staging, true);
                }
            }
        }
        var remote = (await GitRepository.RunAsync(path, ["remote", "get-url", "origin"], cancellationToken)).Trim();
        if (!string.Equals(remote.TrimEnd('/'), entry.Repository.TrimEnd('/'), StringComparison.Ordinal))
            throw new InvalidOperationException($"Case cache remote does not match {entry.Repository}.");
        foreach (var revision in new[] { entry.Before, entry.After })
        {
            if (revision.Length != 40 || revision.Any(c => !Uri.IsHexDigit(c))) throw new InvalidOperationException("Case revisions must be full commit IDs.");
            try { await GitRepository.RunAsync(path, ["cat-file", "-e", revision + "^{commit}"], cancellationToken); }
            catch (InvalidOperationException) { await GitRepository.RunAsync(path, ["fetch", "--filter=blob:none", "origin", revision], cancellationToken); }
            var expectedLicense = revision == entry.Before ? entry.BeforeLicenseBlob : entry.AfterLicenseBlob;
            if (expectedLicense is not null)
            {
                var actualLicense = (await GitRepository.RunAsync(path, ["rev-parse", revision + ":" + entry.LicenseFile], cancellationToken)).Trim();
                if (!actualLicense.Equals(expectedLicense, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"License evidence does not match {entry.Id} at {revision}.");
            }
        }
        return path;
    }

    private static async Task<FileStream> AcquirePreparationAsync(string path, CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (++attempts < 6000) { await Task.Delay(100, cancellationToken); }
        }
    }
}
