using BenchmarkDotNet.Attributes;
using Callrift.Core;
using Callrift.RealWorldCases;

namespace Callrift.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
public class ColdGitBenchmarks
{
    private string server = null!;
    private string revision = null!;
    private string directory = null!;
    private GitRepository repository = null!;
    private IReadOnlyList<GitEntry> entries = null!;
    private IReadOnlyDictionary<string, string> expected = null!;
    private IReadOnlyList<SourceFile>? result;

    [Params("serilog-alignment-guard", "polly-secondary-action")]
    public string Case { get; set; } = "";

    [GlobalSetup]
    public async Task Setup()
    {
        var entry = RealWorldCaseStore.ReadManifest().Single(e => e.Id == Case);
        server = await RealWorldCaseStore.PrepareAsync(entry);
        revision = entry.After;
        var source = new GitRepository(server);
        entries = (await source.ListEntriesAsync(revision)).Where(e => e.Path.StartsWith("src/", StringComparison.Ordinal)
            && e.Path.EndsWith(".cs", StringComparison.Ordinal)).Take(20).ToArray();
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in entries)
            contents.Add(file.Path, (await GitRepository.RunAsync(server, ["cat-file", "blob", file.ObjectId])).TrimStart('\uFEFF'));
        expected = contents;
    }

    [IterationSetup]
    public void PrepareClone()
    {
        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Callrift", "benchmarks", "cold-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Run("init", "--initial-branch=benchmark");
        Run("remote", "add", "archive", new Uri(server + Path.DirectorySeparatorChar).AbsoluteUri);
        Run("config", "remote.archive.promisor", "true");
        Run("config", "remote.archive.partialCloneFilter", "blob:none");
        Run("config", "remote.archive.uploadpack", "git -c uploadpack.allowFilter=true upload-pack");
        Run("fetch", "--depth=1", "--filter=blob:none", "--no-tags", "archive", revision);
        repository = new GitRepository(directory);
        result = null;
    }

    [Benchmark]
    public async Task<IReadOnlyList<SourceFile>> FetchSelectedBlobs() => result = await repository.ReadBlobsAsync(entries);

    [IterationCleanup]
    public void CheckAndRemoveClone()
    {
        try
        {
            if (result is null || result.Count != expected.Count || result.Any(f => !expected.TryGetValue(f.Path, out var content) || content != f.Content))
                throw new InvalidOperationException("Cold Git results differ from the pinned source blobs.");
        }
        finally
        {
            var cacheRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Callrift", "benchmarks"));
            var target = Path.GetFullPath(directory);
            if (!target.StartsWith(cacheRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("Benchmark clone is outside its cache directory.");
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(target, true);
        }
    }

    private void Run(params string[] arguments) => GitRepository.RunAsync(directory, arguments).GetAwaiter().GetResult();
}
