# ASP.NET Core streaming cancellation cleanup

Status: accepted after independent source/output review and a matching two-view repeat.

Pinned pair: `4cab91a40393e7d0341cf43cb88930588a0b4876` to `3563a8e77e0e09055a7f0f18ef9ab026dcd59ad2` in [ASP.NET Core](https://github.com/dotnet/aspnetcore). The before revision is the after revision's parent. Both pins contain the inspected MIT `LICENSE.txt` blob `984713a49622a96da110443c15477613bc12656b`. Source remains in the external Git cache.

The production diff adds one call in `src/SignalR/server/Core/src/Internal/DefaultHubDispatcher.cs`. When `ActiveRequestCancellationSources.TryAdd` rejects a duplicate invocation ID, `StreamAsync` now disposes that invocation's cancellation source before returning. The existing `finally` disposal remains guarded by `ctsRegistered`. The rejected invocation must not remove the cancellation source owned by the existing invocation.

The full method and surrounding cleanup were read before accepting expectations. Focused JSON contains exactly one added metadata call to `CancellationTokenSource.Dispose`, at after line 612, beneath the duplicate-ID guard and after `DefaultHubDispatcherLog.InvocationIdInUse`. The other 113 node sides preserve signatures, bindings, targets, relations, origins, and locations except for the one-line source shift. The existing disposal moves from line 684 to 685. Text and Markdown show the same single conditional addition.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots, depth one | 1,201 | 20,909 | 23,106 | yes |
| source-focused, depth three with externals | 1 | 114 | 23,106 | yes |

The automatic view retains all root identities and ordering. Every before/after node side is unchanged apart from the known source-line shift. Its 3,954 modified nodes are explicit depth omissions toward the edited method; it introduces no other call, target, or signature changes. All 2,433 rendered change lines correspond to those omissions. Source locations and diagnostic locations were checked against immutable blobs, including UTF-16 column bounds. All node IDs are unique, paths are relative, and text agrees with Markdown.

The broad root set reflects conservative source analysis. Graph traversal finds paths from every root to one of the five changed `StreamAsync` contexts. A representative path from an MVC sample controller crosses possible `ToString` implementations, enumeration, stored initialization callbacks, disposal implementations, endpoint registration, and SignalR dispatch. The actual `HttpResponse` initializer tree keeps its disposal body beneath a callback ancestor and reports possible dispatch. These paths do not establish that the sample controller invokes SignalR at runtime. The [source coverage issue](../issues/aspnetcore-source-coverage.md) and [receiver-flow issue](../issues/receiver-flow.md) remain relevant.

The previous incompatible generic comparer path was checked separately on both pins with this implementation. `ModelMetadata.BoundConstructorParameterMapping` supplies `ModelMetadata` as both dictionary arguments; `AdaptiveCapacityDictionary.TryFindItem` retains that binding. Its equality dispatch excludes `CaseSensitiveTagHelperAttributeComparer`, whose contract is `IEqualityComparer<TagHelperAttribute>`. The contravariant `IEqualityComparer<object>` implementation remains a possible target. Both traversals visit nine source members with no context-limit diagnostic. This fixes the specific [generic-context defect](../issues/generic-context.md); it does not prove runtime feasibility of every other possible path.

The explicit restored trial selects `src/SignalR/server/Core/src/Microsoft.AspNetCore.SignalR.Core.csproj` and `net11.0`. It exits 2 because snapshot materialization rejects `src/submodules/MessagePack-CSharp`. Both pins request SDK `11.0.100-rc.1.26420.103`; materialization fails before historical compilation. This case is source-only and does not count toward restored coverage. No source output is presented as a restored result. Cross-platform repeat remains pending.

The isolated repeat passes both views in 7m01s with all four snapshots byte-identical to the reviewed expectations. Frozen parent and worker binaries, sources, review records, and snapshot hashes remain unchanged. The main-harness repeat passes both views in 5m48s without snapshot changes. Matching parent and worker binaries and test inputs were checked during the run and remained unchanged at completion. Cross-platform CI remains pending.
