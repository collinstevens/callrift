using Xunit;
using Xunit.Abstractions;

[assembly: TestCollectionOrderer("Callrift.Workspaces.WorkspaceCollectionOrderer", "Callrift.Workspaces")]

namespace Callrift.Workspaces;

public sealed class WorkspaceCollectionOrderer : ITestCollectionOrderer
{
    private static readonly IReadOnlyDictionary<string, int> Priorities = new[]
    {
        typeof(FrameworkDispatchTests),
        typeof(WorkspaceTests),
        typeof(InterceptorTests),
        typeof(ExplicitInterceptorTests),
        typeof(ImplicitInterceptorTests),
        typeof(ChangedExplicitInterceptorTests),
        typeof(ChangedImplicitInterceptorTests),
        typeof(Framework21DispatchTests),
        typeof(FrameworkWorkspaceTests),
        typeof(SolutionFrameworkWorkspaceTests),
        typeof(MultiTargetFrameworkWorkspaceTests),
        typeof(MultiTargetSolutionFrameworkWorkspaceTests)
    }.Select((type, priority) => (Name: $"Test collection for {type.FullName}", Priority: priority))
        .ToDictionary(item => item.Name, item => item.Priority, StringComparer.Ordinal);

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        testCollections.OrderBy(collection => Priorities.GetValueOrDefault(collection.DisplayName, int.MaxValue))
            .ThenBy(collection => collection.DisplayName, StringComparer.Ordinal);
}
