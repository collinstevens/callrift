using Microsoft.CodeAnalysis;

namespace Callrift.Core;

public sealed record SourceFile(string Path, string Content, string ContentId);

public sealed record SourceSnapshot(string Name, IReadOnlyList<SourceFile> Files);

public sealed record SourceLocation(string Path, int Line, int Column);

public sealed record AnalysisDiagnostic(string Code, string Message, SourceLocation? Location = null);

public sealed record CallStep(
    string Kind,
    string Key,
    string Label,
    bool IsSource,
    SourceLocation Location,
    IReadOnlyList<CallStep> Children);

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
}

public sealed record CallGraph(
    IReadOnlyDictionary<string, Member> Members,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Implementations,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);

public sealed record AnalysisOptions(bool IncludeTests = false);

public interface IAnalysisProvider
{
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
}

public sealed record DiffResult(IReadOnlyList<DiffNode> Trees, IReadOnlyList<AnalysisDiagnostic> Diagnostics, bool HasChanges);
