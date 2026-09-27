using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCaseOrderer("Callrift.RealWorldCases.RealWorldCaseOrderer", "Callrift.RealWorldCases.Tests")]

namespace Callrift.RealWorldCases;

public sealed class RealWorldCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase =>
        testCases.OrderBy(testCase => testCase.TestMethodArguments?.FirstOrDefault() as string, StringComparer.Ordinal)
            .ThenBy(testCase => AnalysisOrder(testCase), StringComparer.Ordinal)
            .ThenBy(testCase => testCase.DisplayName, StringComparer.Ordinal);

    private static string AnalysisOrder(ITestCase testCase)
    {
        if (testCase.TestMethodArguments is not [string id, string viewId]) return "";
        var entry = RealWorldCaseData.Entry(id);
        var view = entry.Views!.Single(view => view.Id == viewId);
        var (options, workspace) = RealWorldCaseFixture.ParseOptions([.. entry.Options, .. view.Options]);
        return workspace is null ? "source" : $"workspace\0{workspace.Target}\0{workspace.Framework}\0{workspace.Configuration}\0{options.IncludeTests}";
    }
}
