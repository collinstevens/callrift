using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Callrift.Scenarios.ScenarioTestFramework", "Callrift.Scenarios")]

namespace Callrift.Scenarios;

public sealed class ScenarioTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new ScenarioTestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}

internal sealed class ScenarioTestFrameworkExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider,
    IMessageSink diagnosticMessageSink) : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
{
    protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
    {
        using var runner = new ScenarioTestAssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
        await runner.RunAsync();
    }
}

internal sealed class ScenarioTestAssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
    : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
{
    protected override async Task BeforeTestAssemblyFinishedAsync()
    {
        await Aggregator.RunAsync(ScenarioWorkspaceFixture.DisposeAsync);
        await base.BeforeTestAssemblyFinishedAsync();
    }
}
