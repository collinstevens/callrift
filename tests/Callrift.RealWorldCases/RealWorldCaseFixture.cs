using System.Globalization;
using System.Text;
using Callrift.Core;
using Callrift.MSBuild;
using VerifyXunit;
using Xunit;

namespace Callrift.RealWorldCases;

public sealed class RealWorldCaseFixture : IDisposable
{
    private AnalyzedPair? cached;

    public Task VerifyHistoryAsync(string id)
    {
        var entry = RealWorldCaseData.Entry(id);
        return SnapshotAsync(entry, entry.Options, id);
    }

    public Task VerifyViewAsync(string id, string viewId)
    {
        var entry = RealWorldCaseData.Entry(id);
        var view = entry.Views!.Single(view => view.Id == viewId);
        Assert.NotNull(entry.Review);
        Assert.True(File.Exists(Path.Combine(RealWorldCaseStore.FindRoot(), "real-world-cases", entry.Review)));
        Assert.NotNull(entry.BeforeLicenseBlob);
        Assert.NotNull(entry.AfterLicenseBlob);
        return SnapshotAsync(entry, [.. entry.Options, .. view.Options], id + "-" + viewId);
    }

    private async Task SnapshotAsync(RealWorldCase entry, string[] arguments, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var repositoryPath = await RealWorldCaseStore.PrepareAsync(entry, timeout.Token);
        var (options, workspace) = ParseOptions(arguments);
        var key = new AnalysisKey(Path.GetFullPath(repositoryPath), entry.Before, entry.After, workspace, options.IncludeTests);
        if (cached?.Key != key)
        {
            cached = null;
            var repository = await GitRepository.OpenAsync(repositoryPath, timeout.Token);
            var beforeId = await repository.ResolveAsync(entry.Before, cancellationToken: timeout.Token);
            var afterId = await repository.ResolveAsync(entry.After, cancellationToken: timeout.Token);
            IAnalysisProvider provider = workspace is null ? new SourceOnlyAnalysisProvider() : new MSBuildAnalysisProvider(workspace);
            var beforeRead = repository.ReadSnapshotAsync(beforeId, cancellationToken: timeout.Token, allFiles: provider.RequiresProjectFiles);
            var afterRead = repository.ReadSnapshotAsync(afterId, cancellationToken: timeout.Token, allFiles: provider.RequiresProjectFiles);
            await Task.WhenAll(beforeRead, afterRead);
            var analysis = new AnalysisOptions(options.IncludeTests);
            var before = provider.AnalyzeAsync(await beforeRead, analysis, timeout.Token);
            var after = provider.AnalyzeAsync(await afterRead, analysis, timeout.Token);
            await Task.WhenAll(before, after);
            cached = new AnalyzedPair(key, await before, await after,
                new SnapshotIdentity("revision", entry.Before, beforeId), new SnapshotIdentity("revision", entry.After, afterId));
        }
        var result = CallriftService.Compare(cached.Before, cached.After, options, timeout.Token) with { From = cached.From, To = cached.To };
        if (entry.Id is "cleanarchitecture-endpoint-groups" or "cleanarchitecture-logging-di" or
            "cleanarchitecture-validation-lambda" or "cleanarchitecture-handler-rename" or "cleanarchitecture-guard-library")
            CleanArchitectureLegacyExpectations.Verify(result, entry.Id);
        if (entry.Id == "orchardcore-esmodule-localization")
            OrchardEsModuleExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "orchardcore-openid-logout")
            OrchardOpenIdExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "orchardcore-elasticsearch-authorization")
            OrchardElasticsearchExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "orchardcore-workflow-startup")
            OrchardWorkflowExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "orchardcore-role-submission")
            OrchardRoleExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "aspnetcore-upload-stream-ownership")
            AspNetUploadStreamExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "aspnetcore-authorization-failure-reasons")
            AspNetSecurityExpectations.VerifyAuthorization(result, options.Entries.Count > 0);
        if (entry.Id == "aspnetcore-two-factor-signout-scheme")
            AspNetSecurityExpectations.VerifySignOut(result, options.Entries.Count > 0);
        if (entry.Id == "aspnetcore-js-task-handling")
            AspNetJsTaskExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "aspnetcore-stream-cts-disposal")
            AspNetStreamCtsExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "serilog-restricted-optional-interfaces")
            SerilogRestrictedSinkExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "serilog-self-metrics")
            SerilogMetricsExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "serilog-metrics-sequencing")
            SerilogSequencingExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "serilog-property-factory")
            SerilogPropertyFactoryExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "serilog-context-collections")
            SerilogContextCollectionExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "serilog-exception-format")
            SerilogAccessorExpectations.VerifyExceptionFormatting(result);
        if (entry.Id == "serilog-extra-arguments")
            SerilogAccessorExpectations.VerifyExtraArguments(result);
        if (entry.Id == "serilog-null-key")
            SerilogAccessorExpectations.VerifyNullKey(result);
        if (entry.Id == "polly-secondary-action")
            PollySecondaryExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-telemetry-source")
            PollyTelemetryExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-caller-cancellation")
            PollyCallerCancellationExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-reload-monitor")
            PollyReloadExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-executor-continuations")
            PollyExecutorExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-fault-null-outcome")
            PollyFaultExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-retry-cancellation")
            PollyRetryExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-timeout-exception-value")
            PollyTimeoutExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-async-overhead")
            PollyAsyncOverheadExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "polly-null-properties")
            PollyNullPropertyExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "cleanarchitecture-value-object-operators")
            CleanArchitectureOperatorExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "cleanarchitecture-static-endpoint-groups")
            CleanArchitectureEndpointExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "cleanarchitecture-previous-page")
            CleanArchitecturePreviousPageExpectations.Verify(result);
        if (entry.Id == "autofac-module-hook-subscriptions")
            AutofacModuleExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "autofac-held-pipeline")
            AutofacHeldPipelineExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "autofac-any-key-cache")
            AutofacAnyKeyExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "autofac-service-key-inheritance")
            AutofacServiceKeyExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "autofac-pipeline-callbacks")
            AutofacPipelineExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "autofac-multi-service-registration")
            AutofacMultiServiceExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "autofac-build-failure-disposal")
            AutofacDisposalExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "autofac-pipeline-subscription-guard")
            AutofacSubscriptionExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-aggregate-route-arrays")
            OcelotAggregationExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-consul-service-address")
            OcelotConsulExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-timeout-status")
            OcelotTimeoutExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-route-claims-keys")
            OcelotRouteClaimsExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-websocket-security")
            OcelotWebSocketExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-websocket-buffer")
            OcelotWebSocketBufferExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-poller-reentrancy")
            OcelotPollerExpectations.Verify(result, options.Entries.Count > 0);
        if (entry.Id == "ocelot-custom-json-merge")
            OcelotJsonMergeExpectations.Verify(result, options.Entries.Count > 0);
        var diagnostics = string.Concat(result.Diagnostics.Take(8).Select(diagnostic =>
            (diagnostic.Location is null ? "" : $"{diagnostic.Location.Path}:{diagnostic.Location.Line}: ") + $"{diagnostic.Code}: {diagnostic.Message}\n"));
        if (result.Diagnostics.Count > 8)
            diagnostics += $"{result.Diagnostics.Count - 8} additional diagnostics; use --diagnostics full.\n";
        string Output(string rendered) => $"exit: 0\nstdout:\n{rendered}stderr:\n{diagnostics}";
        var outputs = new[]
        {
            "format: text\n" + Output(DiffRenderer.Render(result, options)),
            "format: md\n" + Output(DiffRenderer.Render(result, options, markdown: true))
        };
        await Task.WhenAll(VerifyAsync(string.Join("\n", outputs), name),
            VerifyAsync(Output(JsonRenderer.Render(result)), name + "-json"));
    }

    internal static (DiffOptions Options, MSBuildOptions? Workspace) ParseOptions(string[] arguments)
    {
        var entries = new List<string>();
        var files = new List<string>();
        var depth = 6;
        var context = 2;
        var externals = false;
        string? project = null;
        string? framework = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument == "--externals")
            {
                externals = true;
                continue;
            }
            if (++index == arguments.Length) throw new ArgumentException($"Missing value for {argument}.", nameof(arguments));
            var value = arguments[index];
            switch (argument)
            {
                case "--entry": entries.Add(value); break;
                case "--file": files.Add(value); break;
                case "--depth": depth = int.Parse(value, CultureInfo.InvariantCulture); break;
                case "--context":
                    context = value == "all" ? -1 : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0
                        ? count : throw new ArgumentException("Context must be nonnegative or all.", nameof(arguments)); break;
                case "--project": project = value; break;
                case "--framework": framework = value; break;
                default: throw new ArgumentException($"Unsupported snapshot option: {argument}.", nameof(arguments));
            }
        }
        if (depth is < 1 or > 100) throw new ArgumentException("Depth must be between 1 and 100.", nameof(arguments));
        if (framework is not null && project is null) throw new ArgumentException("A framework requires a project.", nameof(arguments));
        return (new DiffOptions { Entries = entries, Files = files, MaxDepth = depth, Context = context, IncludeExternals = externals },
            project is null ? null : new MSBuildOptions(project, framework));
    }

    private static async Task VerifyAsync(string value, string name)
    {
        if (Encoding.UTF8.GetByteCount(value) <= 90 * 1024 * 1024)
        {
            await Verifier.Verify(value).UseDirectory("Snapshots").UseFileName(name).DisableDiff();
            return;
        }
        value = value.ReplaceLineEndings("\n");
        var parts = new List<string>();
        for (var offset = 0; offset < value.Length;)
        {
            var length = Math.Min(16 * 1024 * 1024, value.Length - offset);
            if (offset + length < value.Length)
            {
                var newline = value.LastIndexOf('\n', offset + length - 1, length);
                if (newline < offset) throw new InvalidOperationException("Snapshot line exceeds the part size limit.");
                length = newline - offset + 1;
            }
            var partName = name + "-part-" + (parts.Count + 1).ToString("D3", CultureInfo.InvariantCulture);
            parts.Add(partName);
            await Verifier.Verify(value.Substring(offset, length)).UseDirectory("Snapshots").UseFileName(partName).DisableDiff();
            offset += length;
        }
        await Verifier.Verify(string.Join("\n", parts)).UseDirectory("Snapshots").UseFileName(name).DisableDiff();
    }

    public void Dispose() => cached = null;

    private sealed record AnalysisKey(string Repository, string Before, string After, MSBuildOptions? Workspace, bool IncludeTests);

    private sealed record AnalyzedPair(AnalysisKey Key, CallGraph Before, CallGraph After, SnapshotIdentity From, SnapshotIdentity To);
}
