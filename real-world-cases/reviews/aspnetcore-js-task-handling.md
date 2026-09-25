# ASP.NET Core JavaScript task completion

Status: accepted after independent source/output review and a matching two-view repeat.

Pinned pair: `983abcdf5d7f50f5c3c877ff120b1e975dead25f` to `5f5f31f9a84de3461271a56f726e9b532810b071` in [ASP.NET Core](https://github.com/dotnet/aspnetcore). Both revisions contain the inspected MIT `LICENSE.txt` blob `984713a49622a96da110443c15477613bc12656b`. The pair contains 10,506 C# files and 619 project files, plus TypeScript and Razor inputs. Repository source remains in the external Git cache.

The production dispatcher and task-helper diffs, new record and serialization attribute, and surrounding callers were inspected before acceptance. `InvokeSynchronously` now returns the declared return type through an out parameter. Task continuations pass whether the return type is non-generic. `EndInvokeDotNetAfterTask` gains that parameter, returns after reporting a fault, reports cancellation through `TaskCanceledException`, and obtains a result only for generic tasks. The snapshot preserves the signature change, new cancellation branch, and the moved result-getter call.

`GetTaskByType` now receives the complete ValueTask type. Dynamic-code environments construct a generic conversion delegate; the reflection path finds `AsTask` and returns a callback that invokes it. `TaskGenericsUtil.GetTaskResult` similarly selects between its existing generic getter and a new `ReflectionTaskResultGetter`. Focused JSON and rendered output preserve those branches, the nested callback, and the new possible getter implementation. Runtime reflection targets, actual callback execution, and Native AOT execution are not established by these graphs.

All five automatic roots have independent source support. `ComponentHub.BeginInvokeDotNetFromJS` calls through `CircuitHost` and its nested dispatcher callback. Both `DefaultWebAssemblyJSRuntime` invocation methods reach the changed dispatcher, directly or through `WebAssemblyCallQueue`. The `PhotinoWebViewManager` constructor registers an event callback whose nested task callback reaches `WebViewManager.MessageReceived`, then `IpcReceiver` and the dispatcher. `AsyncInteropResult` is a new record struct in the Native AOT application fixture. Assertion tests are excluded. Razor changes and generated serializer bodies are outside source-only coverage.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots, depth one | 5 | 16 | 22603 | yes |
| source-focused, depth four with externals | 3 | 149 | 22603 | yes |

Diagnostics contain 22,273 unresolved calls, 191 inferred test-project classifications, 84 duplicate members, and 55 parse/preprocessor errors. They remain visible. The [coverage issue](../issues/aspnetcore-source-coverage.md) records source-compilation collisions and other limitations. The focused output's possible `JSRuntime.EndInvokeDotNet` and result-getter targets are not runtime selections.

The location audit checked 323 source-node locations and 45,206 diagnostic locations against immutable Git inputs. These views have no generated locations. Node IDs are unique and paths are relative. Text agrees with Markdown after removing its fences. Independent harness regeneration using the closed-constraint implementation matched both views in text, Markdown, diagnostics, and every JSON field. No source output was normalized to hide a semantic difference.

Both pins select SDK `11.0.100-rc.1.26425.128`. The explicit MSBuild trial selected `src/JSInterop/Microsoft.JSInterop/src/Microsoft.JSInterop.csproj` and `net11.0`; it returned exit 2 because snapshot materialization does not support `src/submodules/MessagePack-CSharp`. This pair is source-only and does not count toward restored coverage. The private two-view repeat passed in 2m12s. The main-harness repeat passed both views in 2m10s without snapshot changes. The previous 79 corpus checks passed against the same Core implementation. Cross-platform repeat remains pending.
