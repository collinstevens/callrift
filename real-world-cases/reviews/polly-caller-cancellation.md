# Polly caller cancellation token propagation

Reviewed and accepted locally: 2026-09-24.

Pinned pair: `27eb2bbb4d45a9570bc35c5ee70419fff4376b25` to `e984839b8324a163270f0bc686929a1066de69e0` in [Polly](https://github.com/App-vNext/Polly). Both revisions contain the inspected BSD-3-Clause LICENSE blob `620c5f1faddf428efafc8049e3e3aee5dbaadcea`. Source and restore assets remain in the external cache.

The new OutcomeUtilities.WithCallerCancellationToken helper checks caller cancellation, an OperationCanceledException outcome, and a different token on that exception. It constructs a replacement exception with the original message and inner exception, sets its stack trace, and creates a replacement outcome. Hedging ExecuteCore calls the helper at two outcome-return sites; Timeout ExecuteCore calls it at its final outcome return. The original hedging cancellation branch and finally disposal remain.

The restored automatic view contains the 21 public synchronous, asynchronous, and outcome-based ResiliencePipeline facade overloads. Their wrappers lead to possible strategy implementations below the depth limit. Source roots also include benchmark helpers and application samples; missing project/package bindings prevent full parity. The focused helper view exposes the complete guard and exception/outcome calls. Restored net8.0 uses ExceptionDispatchInfo.SetCurrentStackTrace; source mode takes the non-NET branch with reflection-based stack-trace helpers. The focused source view is untruncated despite unresolved bindings. The restored view reaches a depth bound inside outcome construction.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source roots | 13 | 173 | 2028 | yes |
| source focused | 1 | 15 | 2028 | yes |
| msbuild roots | 21 | 128 | 15 | yes |
| msbuild focused | 1 | 13 | 15 | yes |

The historical SDK request is 10.0.300 with latestMinor roll-forward. Restored views select Polly.Core at net8.0. The static diff does not prove runtime cancellation timing or exception behavior.

Both modes have automatic depth-one discovery and a deeper focused view. The focused view includes external calls and never substitutes for automatic discovery. Six source diagnostics explain inferred test-project membership; other source diagnostics expose missing dependencies, build-script symbols, and compilation inputs. Restored binding has no unresolved-call diagnostics. Generic invocation limits remain visible.

Text and Markdown agree after removing fences. The shared five-pair audit checked 3,938 node locations and 20,486 diagnostic locations across 1,239 revision/path reads. Regeneration after the syntax-identity fix changed only source labels and diagnostic messages; all ten restored JSON documents stayed equivalent in every field. Graph structure, IDs, signatures, locations, change marks, and diagnostic counts remained stable. The harness generated all 20 views successfully; its 40 snapshot files match the independently reviewed JSON, diagnostics, text, and Markdown trials. These snapshots are accepted locally. All 51 real-world checks passed the complete repeat on Windows in 13 minutes 42 seconds (2026-09-24 22:19:45 run). Formatting and workflow validation pass. Actual three-OS verification of this case checkpoint remains pending.

The [generic-context review](generic-context.md) supersedes the earlier diagnostic counts and truncation flags. Every JSON call tree and rendered stdout body remains identical. The graph now retains instantiated callback state until its explicit expansion bound. Repeated delegating/bridge wrappers grow that state shape; the resulting `generic-context-limit` diagnostics name the affected declarations. Bounds apply to the analyzed graph, so a focused result can report truncation even when its displayed subtree has no omitted child. Coverage is partial.
