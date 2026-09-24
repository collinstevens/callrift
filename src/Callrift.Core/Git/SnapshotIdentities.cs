using System.Security.Cryptography;
using System.Text;

namespace Callrift.Core;

internal static class SnapshotIdentities
{
    public static SnapshotIdentity Create(SourceSnapshot snapshot, string kind, string? reference = null, string? commit = null)
    {
        if (commit is not null) return new SnapshotIdentity(kind, reference, commit);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in snapshot.Files.OrderBy(f => f.Path, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes(file.Path + "\0" + file.ContentId + "\0"));
        return new SnapshotIdentity(kind, reference, ContentId: Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
