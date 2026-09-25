using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Callrift.Core;

public sealed record GitEntry(string Path, string ObjectId);

public sealed class GitRepository(string root)
{
    private readonly SemaphoreSlim fetchGate = new(1, 1);

    public string Root { get; } = root;

    public async Task<string> MergeBaseAsync(string left, string right, CancellationToken cancellationToken = default)
    {
        var bases = (await RunAsync(Root, ["merge-base", "--all", left, right], cancellationToken))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return bases.Length == 1 ? bases[0] : throw new InvalidOperationException("A unique merge base is required.");
    }

    public static async Task<GitRepository> OpenAsync(string directory, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(directory, ["rev-parse", "--show-toplevel"], cancellationToken);
        return new GitRepository(result.TrimEnd('\r', '\n'));
    }

    public async Task<string?> ResolveAsync(string revision, bool allowUnbornHead = false, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(Root, ["rev-parse", "--verify", "--end-of-options", revision + "^{commit}"], cancellationToken);
        if (result.Code == 0)
            return result.Output.Trim();
        if (allowUnbornHead && revision == "HEAD")
        {
            var symbolic = await ExecuteAsync(Root, ["symbolic-ref", "-q", "HEAD"], cancellationToken);
            if (symbolic.Code == 0)
            {
                var exists = await ExecuteAsync(Root, ["show-ref", "--verify", "--quiet", symbolic.Output.Trim()], cancellationToken);
                if (exists.Code == 1)
                    return null;
            }
        }
        throw new InvalidOperationException($"Cannot resolve revision '{revision}'.");
    }

    public async Task<IReadOnlyList<GitEntry>> ListEntriesAsync(string revision, CancellationToken cancellationToken = default, bool allFiles = false)
    {
        var output = await RunAsync(Root, ["ls-tree", "-r", "-z", revision], cancellationToken);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => ParseTreeEntry(e, allFiles)).Where(e => e is not null).Cast<GitEntry>()
            .Where(e => allFiles ? !IsExcluded(e.Path) : IsAnalysisInput(e.Path)).OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
    }

    public async Task<SourceSnapshot> ReadSnapshotAsync(string? revision, bool index = false, CancellationToken cancellationToken = default, bool allFiles = false)
    {
        if (revision is not null)
            return new SourceSnapshot(revision, await ReadBlobsAsync(await ListEntriesAsync(revision, cancellationToken, allFiles), cancellationToken, allFiles));
        if (index)
        {
            var listing = await RunAsync(Root, ["ls-files", "--stage", "-z"], cancellationToken);
            var entries = new List<GitEntry>();
            foreach (var item in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var tab = item.IndexOf('\t');
                var fields = item[..tab].Split(' ');
                if (fields[2] != "0")
                    throw new InvalidOperationException("The index contains unmerged entries.");
                if (allFiles && fields[0] is not ("100644" or "100755") && !IsExcluded(item[(tab + 1)..]))
                    throw new InvalidOperationException("MSBuild snapshots do not support symlinks or submodules: " + item[(tab + 1)..]);
                if (fields[0] == "100644" || fields[0] == "100755")
                    if (allFiles ? !IsExcluded(item[(tab + 1)..]) : IsAnalysisInput(item[(tab + 1)..]))
                        entries.Add(new GitEntry(item[(tab + 1)..], fields[1]));
            }
            return new SourceSnapshot("index", await ReadBlobsAsync(entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray(), cancellationToken, allFiles));
        }
        var paths = await RunAsync(Root, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], cancellationToken);
        var files = new List<SourceFile>();
        foreach (var path in paths.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)
            .Where(p => allFiles ? !IsExcluded(p) : IsAnalysisInput(p)).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(Root, path);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
                continue;
            if (info.LinkTarget is not null)
                throw new InvalidOperationException($"Source symlinks are not supported: {path}");
            var stamp = (info.Length, info.LastWriteTimeUtc);
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            info.Refresh();
            if (!info.Exists || stamp != (info.Length, info.LastWriteTimeUtc))
                throw new InvalidOperationException($"File changed while reading: {path}");
            files.Add(new SourceFile(path, !allFiles || IsAnalysisInput(path) ? Decode(bytes) : "", Convert.ToHexStringLower(SHA256.HashData(bytes)), allFiles ? bytes : null));
        }
        return new SourceSnapshot("working tree", files);
    }

    public async Task<IReadOnlyList<SourceFile>> ReadBlobsAsync(IReadOnlyList<GitEntry> entries, CancellationToken cancellationToken = default, bool preserveBytes = false)
    {
        if (entries.Count == 0)
            return [];
        var noLazyFetch = Environment.GetEnvironmentVariable("GIT_NO_LAZY_FETCH");
        var canFetch = string.IsNullOrEmpty(noLazyFetch) || noLazyFetch.ToLowerInvariant() is "0" or "false" or "no" or "off";
        List<int>? missing = canFetch && entries.All(e => e.ObjectId.Length is 40 or 64 && e.ObjectId.All(Uri.IsHexDigit)) ? [] : null;
        var available = await ReadAvailableBlobsAsync(entries, missing, cancellationToken, preserveBytes);
        if (missing is null || missing.Count == 0)
            return available;
        var required = missing.Select(i => entries[i]).ToArray();
        await FetchMissingBlobsAsync(required, cancellationToken);
        var fetched = await ReadAvailableBlobsAsync(required, null, cancellationToken, preserveBytes);
        var files = new SourceFile[entries.Count];
        var fetchedIndex = 0;
        var availableIndex = 0;
        for (var i = 0; i < files.Length; i++)
            files[i] = fetchedIndex < missing.Count && missing[fetchedIndex] == i ? fetched[fetchedIndex++] : available[availableIndex++];
        return files;
    }

    private async Task<IReadOnlyList<SourceFile>> ReadAvailableBlobsAsync(IReadOnlyList<GitEntry> entries, List<int>? missing,
        CancellationToken cancellationToken, bool preserveBytes)
    {
        using var process = Start(Root, ["cat-file", "--batch"], noLazyFetch: missing is not null);
        using var registration = cancellationToken.Register(() => Kill(process));
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        var writer = Task.Run(async () =>
        {
            foreach (var entry in entries)
                await process.StandardInput.WriteLineAsync(entry.ObjectId.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }, cancellationToken);
        var files = new List<SourceFile>(entries.Count);
        var stream = process.StandardOutput.BaseStream;
        try
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var header = await ReadLineAsync(stream, cancellationToken);
                var fields = header.Split(' ');
                if (missing is not null && fields.Length == 2 && fields[1] == "missing" && fields[0].Equals(entry.ObjectId, StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add(i);
                    continue;
                }
                if (fields.Length != 3 || fields[1] != "blob" || !int.TryParse(fields[2], out var size) || size < 0)
                    throw new InvalidOperationException($"Cannot read Git blob for {entry.Path}: {header}");
                var bytes = new byte[size];
                await stream.ReadExactlyAsync(bytes, cancellationToken);
                if (await ReadLineAsync(stream, cancellationToken) != "")
                    throw new InvalidOperationException("Invalid git cat-file framing.");
                files.Add(new SourceFile(entry.Path, !preserveBytes || IsAnalysisInput(entry.Path) ? Decode(bytes) : "", entry.ObjectId, preserveBytes ? bytes : null));
            }
            await writer;
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(await errors);
            return files;
        }
        finally
        {
            Kill(process);
            try { await writer; } catch (IOException) { }
        }
    }

    private async Task FetchMissingBlobsAsync(IReadOnlyList<GitEntry> entries, CancellationToken cancellationToken)
    {
        await fetchGate.WaitAsync(cancellationToken);
        try
        {
            var missing = await FindMissingBlobsAsync(entries.Select(e => e.ObjectId).Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
            if (missing is null || missing.Length == 0)
                return;
            var config = await ExecuteAsync(Root, ["config", "--null", "--get-regexp", "^(remote\\..*\\.(promisor|partialclonefilter)|extensions\\.partialclone)$"], cancellationToken);
            if (config.Code != 0)
                return;
            var remotes = new List<string>();
            string? preferredLast = null;
            foreach (var record in config.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = record.Split('\n', 2);
                var key = fields[0];
                var value = fields.Length == 2 ? fields[1] : null;
                if (key == "extensions.partialclone")
                    preferredLast = value;
                else if (key.StartsWith("remote.", StringComparison.Ordinal))
                {
                    var suffix = key.LastIndexOf('.');
                    var remote = key[7..suffix];
                    var enabled = key[(suffix + 1)..] == "partialclonefilter" || value is null
                        || value.ToLowerInvariant() is "true" or "yes" or "on"
                        || long.TryParse(value, out var number) && number != 0;
                    if (enabled && !remotes.Contains(remote, StringComparer.Ordinal))
                        remotes.Add(remote);
                }
            }
            if (!string.IsNullOrEmpty(preferredLast))
            {
                remotes.Remove(preferredLast);
                remotes.Add(preferredLast);
            }
            string? error = null;
            foreach (var remote in remotes)
            {
                var result = await ExecuteAsync(Root, ["-c", "fetch.negotiationAlgorithm=noop", "fetch", "--no-tags", "--no-write-fetch-head",
                    "--recurse-submodules=no", "--filter=blob:none", "--stdin", "--", remote], cancellationToken, string.Join('\n', missing) + "\n");
                error = result.Error;
                missing = await FindMissingBlobsAsync(missing, cancellationToken);
                if (missing is null || missing.Length == 0)
                    return;
            }
            if (remotes.Count > 0)
                throw new InvalidOperationException("Cannot fetch required Git blobs: " + error?.Trim());
        }
        finally
        {
            fetchGate.Release();
        }
    }

    private async Task<string[]?> FindMissingBlobsAsync(string[] ids, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(Root, ["--no-lazy-fetch", "cat-file", "--batch-check=%(objectname) %(objecttype)", "--buffer"],
            cancellationToken, string.Join('\n', ids) + "\n");
        if (result.Code != 0)
            return null;
        var requested = ids.ToHashSet(StringComparer.Ordinal);
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.EndsWith(" missing", StringComparison.Ordinal)).Select(line => line[..^8])
            .Where(requested.Contains).ToArray();
    }

    public static async Task<string> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(directory, arguments, cancellationToken);
        if (result.Code != 0)
            throw new InvalidOperationException($"git {arguments[0]} failed: {result.Error.Trim()}");
        return result.Output;
    }

    private static async Task<(int Code, string Output, string Error)> ExecuteAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? input = null)
    {
        using var process = Start(directory, arguments);
        using var registration = cancellationToken.Register(() => Kill(process));
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        var write = WriteInputAsync(process, input, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            try { await write; } catch (IOException) when (process.ExitCode != 0) { }
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            Kill(process);
            try { await write; } catch (IOException) { }
        }
    }

    private static async Task WriteInputAsync(Process process, string? input, CancellationToken cancellationToken)
    {
        try
        {
            if (input is not null)
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
        }
        finally
        {
            process.StandardInput.Close();
        }
    }

    private static Process Start(string directory, IReadOnlyList<string> arguments, bool noLazyFetch = false)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (noLazyFetch)
            start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var next = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(next, cancellationToken);
            if (next[0] == 10)
                return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(next[0]);
        }
    }

    private static GitEntry? ParseTreeEntry(string entry, bool allFiles)
    {
        var tab = entry.IndexOf('\t');
        var fields = entry[..tab].Split(' ');
        if (allFiles && fields[0] is not ("100644" or "100755") && !IsExcluded(entry[(tab + 1)..]))
            throw new InvalidOperationException("MSBuild snapshots do not support symlinks or submodules: " + entry[(tab + 1)..]);
        return fields[0] is "100644" or "100755" ? new GitEntry(entry[(tab + 1)..], fields[2]) : null;
    }

    private static string Decode(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

    private static bool IsAnalysisInput(string path) =>
        !IsExcluded(path) &&
        (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase));

    private static bool IsExcluded(string path) => path.Split('/').Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("obj", StringComparison.OrdinalIgnoreCase));
}
