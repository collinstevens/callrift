using Xunit;
using Xunit.Abstractions;

[assembly: TestCollectionOrderer("Callrift.Scenarios.ScenarioCollectionOrderer", "Callrift.Scenarios")]

namespace Callrift.Scenarios;

public sealed class ScenarioCollectionOrderer : ITestCollectionOrderer
{
    private static readonly IReadOnlyDictionary<string, int> Priorities = new[]
    {
        typeof(ConstraintDispatchTests),
        typeof(StaticInitializationTests),
        typeof(ReceiverContextTests),
        typeof(ConstructorInitializationTests),
        typeof(GenericContextTests),
        typeof(RecordCopyTests),
        typeof(ConstructorEquivalenceTests),
        typeof(StaticInitializationDeclarationTests),
        typeof(VarianceCompatibilityTests),
        typeof(DispatchCardinalityTests),
        typeof(RecordCopyValidationTests),
        typeof(DepthVisibilityTests)
    }.Select((type, priority) => (Name: $"Test collection for {type.FullName}", Priority: priority))
        .ToDictionary(item => item.Name, item => item.Priority, StringComparer.Ordinal);

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        testCollections.OrderBy(collection => Priorities.GetValueOrDefault(collection.DisplayName, int.MaxValue))
            .ThenBy(collection => collection.DisplayName, StringComparer.Ordinal);
}
