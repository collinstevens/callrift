using System.Text.Json;
using Callrift.Core;

namespace Callrift.Corpus;

public sealed record CorpusEntry(string Id, string Repository, string CacheName, string License, string LicenseFile,
    string Before, string After, string Reason, string[] Tags, string[] Options, string? KnownIssue);

public static class CorpusStore
{
    public static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "corpus", "manifest.json"))) return directory.FullName;
        throw new InvalidOperationException("Cannot find corpus/manifest.json.");
    }

    public static IReadOnlyList<CorpusEntry> ReadManifest() => JsonSerializer.Deserialize<CorpusEntry[]>(File.ReadAllText(Path.Combine(FindRoot(), "corpus", "manifest.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static async Task<string> PrepareAsync(CorpusEntry entry, CancellationToken cancellationToken = default)
    {
        var cache = Environment.GetEnvironmentVariable("CALLRIFT_CORPUS_CACHE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Callrift", "corpus");
        Directory.CreateDirectory(cache);
        if (entry.CacheName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || entry.CacheName.Contains('/') || entry.CacheName.Contains('\\') || entry.CacheName is "." or "..")
            throw new InvalidOperationException("Invalid corpus cache name.");
        var path = Path.Combine(cache, entry.CacheName);
        if (!Directory.Exists(path))
            await GitRepository.RunAsync(cache, ["clone", "--filter=blob:none", "--no-checkout", entry.Repository, entry.CacheName], cancellationToken);
        var remote = (await GitRepository.RunAsync(path, ["remote", "get-url", "origin"], cancellationToken)).Trim();
        if (!string.Equals(remote.TrimEnd('/'), entry.Repository.TrimEnd('/'), StringComparison.Ordinal))
            throw new InvalidOperationException($"Corpus cache remote does not match {entry.Repository}.");
        foreach (var revision in new[] { entry.Before, entry.After })
        {
            if (revision.Length != 40 || revision.Any(c => !Uri.IsHexDigit(c))) throw new InvalidOperationException("Corpus revisions must be full commit IDs.");
            try { await GitRepository.RunAsync(path, ["cat-file", "-e", revision + "^{commit}"], cancellationToken); }
            catch (InvalidOperationException) { await GitRepository.RunAsync(path, ["fetch", "--filter=blob:none", "origin", revision], cancellationToken); }
        }
        return path;
    }
}
