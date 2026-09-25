# Polly scheduled completion-source overload

Reviewed and accepted locally: 2026-09-24.

Pinned pair: `779aa834d9a317a7e26493385f2fdf48ce20f3ef` to `016dd909db880b9d8d07954ff5f4ea3cc1e11c67` in [Polly](https://github.com/App-vNext/Polly). Both revisions contain the inspected BSD-3-Clause LICENSE blob `620c5f1faddf428efafc8049e3e3aee5dbaadcea`. Source and restore assets remain in the external cache.

ScheduledTaskExecutor.ScheduleTask replaces TaskCompletionSource<object> construction with the overload taking TaskCreationOptions.RunContinuationsAsynchronously. Queue insertion, Entry construction, semaphore release, and the returned Task remain. The disposed check uses ObjectDisposedException.ThrowIf under NET8_0_OR_GREATER and an explicit conditional throw in the source-mode branch.

The focused views preserve surrounding calls and show one removed parameterless constructor plus one added TaskCreationOptions constructor. Their JSON metadata identities distinguish the overloads even though both text labels name the same type. Restored automatic discovery contains 21 public pipeline facade overloads plus two AddCircuitBreaker overloads. The latter retain CreateStrategy callbacks; CircuitBreakerResilienceStrategy construction registers IsolateCircuitAsync and CloseCircuitAsync method groups with manual control. CircuitStateController schedules opened, closed, and half-open callbacks through the changed executor. Those source paths were read independently. Source automatic roots are benchmark/helper callers above the loaded library methods.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source roots | 9 | 108 | 2023 | yes |
| source focused | 1 | 8 | 2023 | yes |
| msbuild roots | 23 | 138 | 14 | yes |
| msbuild focused | 1 | 7 | 14 | yes |

The historical SDK request is 10.0.103 with latestMinor roll-forward. Restored views select Polly.Core at net8.0. Focused views are untruncated. The output establishes the overload change and possible callback paths, not queue concurrency or continuation scheduling at runtime.

Both modes have automatic depth-one discovery and a deeper focused view. The focused view includes external calls and never substitutes for automatic discovery. Six source diagnostics explain inferred test-project membership; other source diagnostics expose missing dependencies, build-script symbols, and compilation inputs. Restored binding has no unresolved-call diagnostics. Generic invocation limits remain visible.

Text and Markdown agree after removing fences. The shared five-pair audit checked 3,938 node locations and 20,486 diagnostic locations across 1,239 revision/path reads. Regeneration after the syntax-identity fix changed only source labels and diagnostic messages; all ten restored JSON documents stayed equivalent in every field. Graph structure, IDs, signatures, locations, change marks, and diagnostic counts remained stable. The harness generated all 20 views successfully; its 40 snapshot files match the independently reviewed JSON, diagnostics, text, and Markdown trials. These snapshots are accepted locally. All 51 real-world checks passed the complete repeat on Windows in 13 minutes 42 seconds (2026-09-24 22:19:45 run). Formatting and workflow validation pass. Actual three-OS verification of this case checkpoint remains pending.

The [generic-context review](generic-context.md) supersedes the earlier diagnostic counts and truncation flags. Every JSON call tree and rendered stdout body remains identical. The graph now retains instantiated callback state until its explicit expansion bound. Repeated delegating/bridge wrappers grow that state shape; the resulting `generic-context-limit` diagnostics name the affected declarations. Bounds apply to the analyzed graph, so a focused result can report truncation even when its displayed subtree has no omitted child. Coverage is partial.

The [receiver-context review](receiver-context.md) records the subsequent inherited-call changes, diagnostic updates, and source traces for this pair. Its current counts supersede the earlier snapshot counts above.
