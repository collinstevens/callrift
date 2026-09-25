# Polly shared telemetry source and pipeline construction

Reviewed and accepted locally: 2026-09-24.

Pinned pair: `af7e8b16a0c032149021693d326860c00e48fd0e` to `b87f20347af528b6f7c6ff1f9e6c1bc02f97856c` in [Polly](https://github.com/App-vNext/Polly). Both revisions contain the inspected BSD-3-Clause LICENSE blob `620c5f1faddf428efafc8049e3e3aee5dbaadcea`. Source and restore assets remain in the external cache.

This pair changes Polly.Core and Polly.Extensions. RegistryPipelineComponentBuilder moves an explicit constructor into a primary constructor with equivalent field assignments. ResiliencePipelineBuilderBase extracts telemetry construction into local variables in two methods. TelemetryListenerImpl obtains its meter from a new shared TelemetrySource. The new private constructor calls GetVersion and constructs Meter. GetVersion reads assembly metadata and removes Git and prerelease suffixes through two IndexOf/Substring guards.

Restored automatic roots cover two registry entry overloads, two pipeline Build methods, three DI registration entry overloads, ConfigureTelemetry, and the new TelemetrySource constructor. Source mode adds benchmark helpers and application samples. The focused constructor is selected by its actual label, new TelemetrySource; a bare type name is not a valid entry selector. Its ten nodes agree in structure across modes, with unresolved reflection/string bindings explicit in source mode. NET selects the StringComparison IndexOf overload in the restored view.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source roots | 17 | 243 | 2028 | yes |
| source focused | 1 | 10 | 2028 | yes |
| msbuild roots | 9 | 33 | 14 | yes |
| msbuild focused | 1 | 10 | 14 | yes |

The historical SDK request is 10.0.201 with latestMinor roll-forward. Restored views select Polly.Extensions and its Polly.Core reference at net8.0. The graph does not follow the static Instance field initialization or Meter property access. The private constructor appearing as a root is an explicit coverage gap, not a runtime application entry point. Track that missing connection in the [repository-specific coverage issue](../issues/polly-coverage.md).

Both modes have automatic depth-one discovery and a deeper focused view. The focused view includes external calls and never substitutes for automatic discovery. Six source diagnostics explain inferred test-project membership; other source diagnostics expose missing dependencies, build-script symbols, and compilation inputs. Restored binding has no unresolved-call diagnostics. Generic invocation limits remain visible.

Text and Markdown agree after removing fences. The shared five-pair audit checked 3,938 node locations and 20,486 diagnostic locations across 1,239 revision/path reads. Regeneration after the syntax-identity fix changed only source labels and diagnostic messages; all ten restored JSON documents stayed equivalent in every field. Graph structure, IDs, signatures, locations, change marks, and diagnostic counts remained stable. The harness generated all 20 views successfully; its 40 snapshot files match the independently reviewed JSON, diagnostics, text, and Markdown trials. These snapshots are accepted locally. All 51 real-world checks passed the complete repeat on Windows in 13 minutes 42 seconds (2026-09-24 22:19:45 run). Formatting and workflow validation pass. Actual three-OS verification of this case checkpoint remains pending.

The [generic-context review](generic-context.md) supersedes the earlier diagnostic counts and truncation flags. Every JSON call tree and rendered stdout body remains identical. The graph now retains instantiated callback state until its explicit expansion bound. Repeated delegating/bridge wrappers grow that state shape; the resulting `generic-context-limit` diagnostics name the affected declarations. Bounds apply to the analyzed graph, so a focused result can report truncation even when its displayed subtree has no omitted child. Coverage is partial.
