# Polly secondary action extraction and telemetry construction

Reviewed and accepted locally: 2026-09-24.

Pinned pair: `e6582c39e7ba10c8fbcddbfefccd7a09e8dd8aab` to `1a80392b1f093f40e59c515f4aeb989bea5db857` in [Polly](https://github.com/App-vNext/Polly). Both revisions contain the inspected BSD-3-Clause LICENSE blob `620c5f1faddf428efafc8049e3e3aee5dbaadcea`. Source and restore assets remain in the external cache.

The xunit-update commit includes production and build-script changes. TaskExecution.InitializeAsync extracts secondary action creation into TryCreateSecondaryActionAsync. The helper retains the default-generator early return, null-action reset, exception-to-outcome path, and early-result tuple. TelemetryOptions copy construction gains this(). The conditional legacy ConcurrentDictionaryExtensions.GetOrAdd helper is deleted. RetryHelper and TelemetryUtil edits are comments only. cake.cs adds coverage tasks and local helpers; source mode must keep those build-script changes visible.

Source automatic roots are TaskExecution<T>.InitializeAsync, the telemetry copy constructor, the deleted dictionary helper, three coverage-reading local functions, and the cake top-level method. The AOT test-directory executable is retained by classification; its Program.cs only prints Hello Polly and has no affected library call. Restored discovery contains the 21 pipeline facade overloads and telemetry copy constructor. Focused InitializeAsync views preserve context initialization, cancellation registration, primary/secondary execution branches, telemetry callbacks, and exception paths around the helper extraction. Source mode cannot resolve the cancellation-source pool call and selects CancellationToken.Register; restored net8.0 resolves the pool and UnsafeRegister. The helper early-return guard has no contained call and is omitted under the current syntax-tree scope; the graph does not infer a downstream control-flow predicate.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source roots | 7 | 224 | 2205 | yes |
| source focused | 1 | 75 | 2205 | yes |
| msbuild roots | 22 | 131 | 0 | yes |
| msbuild focused | 1 | 96 | 0 | yes |

The historical SDK request is 10.0.401, already installed. Restored views select Polly.Extensions and Polly.Core at net8.0. Source mode includes the Cake script without its build environment, so its unresolved calls remain explicit. The telemetry callback cycle is a possible static type-level cycle, not proof of runtime recursion.

Both modes have automatic depth-one discovery and a deeper focused view. The focused view includes external calls and never substitutes for automatic discovery. Six source diagnostics explain inferred test-project membership; other source diagnostics expose missing dependencies, build-script symbols, and compilation inputs. All restored views have zero diagnostics.

Text and Markdown agree after removing fences. The shared five-pair audit checked 3,938 node locations and 20,486 diagnostic locations across 1,239 revision/path reads. Regeneration after the syntax-identity fix changed only source labels and diagnostic messages; all ten restored JSON documents stayed equivalent in every field. Graph structure, IDs, signatures, locations, change marks, and diagnostic counts remained stable. The harness generated all 20 views successfully; its 40 snapshot files match the independently reviewed JSON, diagnostics, text, and Markdown trials. These snapshots are accepted locally. All 51 real-world checks passed the complete repeat on Windows in 13 minutes 42 seconds (2026-09-24 22:19:45 run). Formatting and workflow validation pass. Actual three-OS verification of this case checkpoint remains pending.
