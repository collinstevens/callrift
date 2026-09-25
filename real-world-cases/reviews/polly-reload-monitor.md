# Polly monitor-based pipeline reloads

Reviewed and accepted locally: 2026-09-24.

Pinned pair: `6cc79edccae5d6f236fd011a69fb12e8f913be80` to `f2a07e687b3020f6cbaaf9192f14e1f8845682e3` in [Polly](https://github.com/App-vNext/Polly). Both revisions contain the inspected BSD-3-Clause LICENSE blob `620c5f1faddf428efafc8049e3e3aee5dbaadcea`. Source and restore assets remain in the external cache.

The public EnableReloads method still resolves IOptionsMonitor from its service provider. Its call to ConfigureBuilderContextExtensions.EnableReloads moves beneath the new EnableReloadsWithMonitor helper. The helper adds Guard.NotNull for the supplied monitor. The focused diff must retain the existing change-name guard, cancellation-token registration, and disposal callback beneath the relocated extension call.

Both automatic modes discover one affected root, AddResiliencePipelineContext<TKey>.EnableReloads. At depth one, the old direct extension call is removed and the new helper is added. The focused views at depth four expose the relocated callback subtree. Guard.NotNull uses a conditional throw in source mode and ArgumentNullException.ThrowIfNull in the selected restored framework. Source mode includes additional possible IDisposable implementations from the legacy Polly and rate-limiting projects; restored mode includes only Polly.Extensions and its Polly.Core reference. These are possible dispatch targets, not claims about the monitor registration returned at runtime.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source roots | 1 | 3 | 2005 | yes |
| source focused | 1 | 68 | 2005 | yes |
| msbuild roots | 1 | 3 | 0 | yes |
| msbuild focused | 1 | 50 | 0 | yes |

The historical SDK request is 10.0.302 without prerelease selection; installed SDK 10.0.303 satisfies its patch roll-forward. Restored views select Polly.Extensions at net8.0. Callback order and actual options-change delivery are outside this static result.

Both modes have automatic depth-one discovery and a deeper focused view. The focused view includes external calls and never substitutes for automatic discovery. Six source diagnostics explain inferred test-project membership; other source diagnostics expose missing dependencies, build-script symbols, and compilation inputs. All restored views have zero diagnostics.

Text and Markdown agree after removing fences. The shared five-pair audit checked 3,938 node locations and 20,486 diagnostic locations across 1,239 revision/path reads. Regeneration after the syntax-identity fix changed only source labels and diagnostic messages; all ten restored JSON documents stayed equivalent in every field. Graph structure, IDs, signatures, locations, change marks, and diagnostic counts remained stable. The harness generated all 20 views successfully; its 40 snapshot files match the independently reviewed JSON, diagnostics, text, and Markdown trials. These snapshots are accepted locally. All 51 real-world checks passed the complete repeat on Windows in 13 minutes 42 seconds (2026-09-24 22:19:45 run). Formatting and workflow validation pass. Actual three-OS verification of this case checkpoint remains pending.
