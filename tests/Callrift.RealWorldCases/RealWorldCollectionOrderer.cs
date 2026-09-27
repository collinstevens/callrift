using Xunit;
using Xunit.Abstractions;

[assembly: TestCollectionOrderer("Callrift.RealWorldCases.RealWorldCollectionOrderer", "Callrift.RealWorldCases.Tests")]

namespace Callrift.RealWorldCases;

public sealed class RealWorldCollectionOrderer : ITestCollectionOrderer
{
    private static readonly IReadOnlyDictionary<string, int> Priorities = new[]
    {
        typeof(SerilogRealWorldCaseTests),
        typeof(AutofacRealWorldCaseTests),
        typeof(CleanArchitectureRealWorldCaseTests),
        typeof(SweepProcessTests),
        typeof(OrchardCoreRealWorldCaseTests),
        typeof(PollyRealWorldCaseTests),
        typeof(RealWorldCaseTests),
        typeof(OcelotRealWorldCaseTests),
        typeof(AspNetCoreRealWorldCaseTests)
    }.Select((type, priority) => (Name: $"Test collection for {type.FullName}", Priority: priority))
        .ToDictionary(item => item.Name, item => item.Priority, StringComparer.Ordinal);

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        testCollections.OrderBy(collection => Priorities.GetValueOrDefault(collection.DisplayName, int.MaxValue))
            .ThenBy(collection => collection.DisplayName, StringComparer.Ordinal);
}
