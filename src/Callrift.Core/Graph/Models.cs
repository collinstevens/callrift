using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Callrift.Core;

public sealed record SourceFile(string Path, string Content, string ContentId, byte[]? RawBytes = null);

public sealed record SourceSnapshot(string Name, IReadOnlyList<SourceFile> Files);

public sealed record SourceLocation(string Path, [property: JsonPropertyName("startLine")] int Line,
    [property: JsonPropertyName("startColumn")] int Column, int? EndLine = null, int? EndColumn = null);

public sealed record AnalysisDiagnostic(string Code, string Message, SourceLocation? Location = null);

public sealed record AnalysisCoverage(string Mode, string Status, IReadOnlyList<string> Limitations)
{
    public static AnalysisCoverage SourceOnly { get; } = new("source", "partial",
        ["single-compilation", "bcl-references-only", "synthetic-implicit-usings", "no-project-defines-or-generators", "possible-dispatch", "unfollowed-accessors-operators-events", "callbacks-are-possible-calls"]);
}

public sealed record CallStep(
    string Kind,
    string Key,
    string Label,
    bool IsSource,
    SourceLocation Location,
    IReadOnlyList<CallStep> Children)
{
    public string Relation { get; init; } = "call";
    public bool SuppressDispatch { get; init; }
    public DispatchType? DispatchType { get; init; }
    public DispatchType? ReceiverType { get; init; }
    public IReadOnlyList<string> Candidates { get; init; } = [];
}

public sealed record NodeSide(string? SymbolId, string? Signature, string Binding, string Dispatch,
    IReadOnlyList<string> TargetIds, SourceLocation? Definition, IReadOnlyList<SourceLocation> CallSites,
    string Relation = "call", IReadOnlyList<string>? Candidates = null)
{
    public string Origin { get; init; } = "source";
}

public sealed record Omission(string Reason, string? SymbolId = null);

public sealed record SnapshotIdentity(string Kind, string? Ref = null, string? Commit = null, string? ContentId = null);

public sealed record Member(
    string Key,
    string Label,
    string MatchName,
    string Signature,
    SourceLocation Location,
    bool HasBody,
    IReadOnlyList<CallStep> Calls)
{
    internal SyntaxNode? Body { get; init; }
    public string? BodyFingerprint { get; init; }
}

public sealed record CallGraph(
    IReadOnlyDictionary<string, Member> Members,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Implementations,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics)
{
    public AnalysisCoverage Coverage { get; init; } = AnalysisCoverage.SourceOnly;
    public IReadOnlyDictionary<string, IReadOnlyList<DispatchContract>> DispatchContracts { get; init; } = new Dictionary<string, IReadOnlyList<DispatchContract>>();

    public IReadOnlyList<string> Targets(CallStep call)
    {
        if (call.SuppressDispatch || !Implementations.TryGetValue(call.Key, out var targets)) return [];
        if (call.DispatchType is null || !DispatchContracts.TryGetValue(call.Key, out var contracts)) return targets;
        return contracts.Where(c => c.CanMatch(call.DispatchType, call.ReceiverType)).Select(c => c.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}

public sealed record AnalysisOptions(bool IncludeTests = false);

public interface IAnalysisProvider
{
    bool RequiresProjectFiles => false;
    Task<CallGraph> AnalyzeAsync(SourceSnapshot snapshot, AnalysisOptions options, CancellationToken cancellationToken = default);
}

public sealed record DiffOptions
{
    public IReadOnlyList<string> Entries { get; init; } = [];
    public IReadOnlyList<string> Files { get; init; } = [];
    public IReadOnlyList<string> Paths { get; init; } = [];
    public int MaxDepth { get; init; } = 6;
    public int Context { get; init; } = 2;
    public bool IncludeExternals { get; init; }
    public bool IncludeTests { get; init; }
    public bool Locations { get; init; }
}

public sealed record DiffRequest(string Repository, string? Before = null, string? After = null, bool Staged = false)
{
    public DiffOptions Options { get; init; } = new();
}

public sealed record DiffNode(
    string Key,
    string Label,
    char Mark,
    IReadOnlyList<DiffNode> Children,
    string? Detail = null)
{
    public bool HasChanges => Mark != ' ' || Children.Any(c => c.HasChanges);
    public string Kind { get; init; } = "call";
    public NodeSide? Before { get; init; }
    public NodeSide? After { get; init; }
    public Omission? Omission { get; init; }
}

public sealed record DiffResult(IReadOnlyList<DiffNode> Trees, IReadOnlyList<AnalysisDiagnostic> Diagnostics, bool HasChanges)
{
    public string Command { get; init; } = "diff";
    public SnapshotIdentity? From { get; init; }
    public SnapshotIdentity? To { get; init; }
    public bool Truncated { get; init; }
    public AnalysisCoverage Coverage { get; init; } = AnalysisCoverage.SourceOnly;
}
