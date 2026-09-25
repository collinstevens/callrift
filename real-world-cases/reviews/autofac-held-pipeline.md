# Autofac held pipeline review

Status: source and snapshot review complete. Local repeatability and cross-platform execution are checked separately below.

Repository: https://github.com/autofac/Autofac. Before: `dc2252d59922c8b62f8305edabce96fe6211f786`. After: `ae9e1e1129b9c22e7ab111381308dcb02f80a8d2`. Both `LICENSE` files are MIT text at blob `e89fb634a4319b9488446ec6fb04e58cdbb1f2ad`. The case runner checks those immutable license identities.

The source diff removes the `_ephemeralServiceInfo is null` guard and the nested `registration.BuildResolvePipeline(...)` call from `HoldForAdditionalService`. The outer `additionalInfo.IsSourceQueued(source)` guard and its `DeferSourceImplementation` call remain. The `!additionalInfo.IsInitializing` branch still calls `BeginServiceInfoInitialization`. The calling `HoldOrApplyForAdditionalService` retains its monitor guard and try/finally.

The upstream motivation is event ordering for registrations with additional services. callrift does not model event dispatch and cannot prove that ordering. This case reviews the explicit calls and their guards.

Four views pair complete automatic root discovery at depth one with a focused method diff at depth three, in source and MSBuild modes. Both limits are explicit in the manifest and reported as truncation. Source mode reads Git objects from the external no-checkout clone. MSBuild mode uses `src/Autofac/Autofac.csproj` at net10.0, preserves the netstandard2.0 generator reference, and uses mise's installed SDK 10.0.401. Source and restore assets remain outside this checkout.

The loader and delegate-construction investigations are recorded in [candidate investigation](candidate-investigation.md). Review also exposed incompatible generic dispatch from a collection of callbacks to `FallbackDictionary<TKey, TValue>.Add`, and a false `Container.GetService` self-cycle through an `IServiceProvider` cast. Both were fixed before snapshot acceptance. The latter now targets only `LifetimeScope.GetService`, consistent with the declared type of `_rootLifetimeScope` and its constructor assignment.

## Independent expectations and output review

The source diff and surrounding tracker, container, middleware, registration, and resolution code were read from the pinned Git objects. The focused views contain 18 nodes each. Four removed nodes represent the inner guard, interface pipeline call, and its two implementation candidates. `BeginServiceInfoInitialization`, `IsSourceQueued`, and `DeferSourceImplementation` remain in source order. The deferred-map constructor remains under the preserved branch. The call to initialization stays on line 684; deferred implementation moves from line 697 to 692 and the initialization declaration moves from 707 to 702. Modified depth markers on middleware candidates describe changes reachable below the limit, not edits to those candidate bodies.

Automatic roots were reviewed across public registration and resolution APIs, reflection activation, property injection, modules, callbacks, and benchmarks. Source mode includes repository-wide non-test C# inputs, including benchmarks and the AOT warning sample. The restored view selects the library project and includes generated delegate registration overloads. The different root counts reflect those input scopes. Twenty generated registration overloads have distinct declaration identities. Neither view relies on a focused selector to hide discovery results.

| View | Roots | Nodes | Diagnostics | Depth omissions | Cycles |
|---|---:|---:|---:|---:|---:|
| Source automatic | 132 | 732 | 172 | 556 | 0 |
| Source focused | 1 | 18 | 172 | 6 | 0 |
| MSBuild automatic | 99 | 369 | 0 | 247 | 0 |
| MSBuild focused | 1 | 18 | 0 | 6 | 0 |

All four views explicitly report truncation. The 172 source diagnostics comprise 25 in benchmarks, 13 in the generator project, and 134 in library sources. They expose unavailable package and generated symbols, including BenchmarkDotNet, Roslyn, generated registration overloads, and resource properties. Unknown resource expression types also prevent binding some formatting and exception-constructor overloads. The restored views have no unresolved diagnostics. Source-mode incompleteness is visible and is not treated as restored-mode parity.

Text and Markdown trees were read and compared; their content agrees after removing the Markdown diff fence. JSON review checked the focused node structure, automatic root identities, dispatch candidates, preserved and removed calls, ordering, diagnostics, omissions, and cycle flags against those trees. Node identifiers are unique within each document and revision identities are the full pinned commits. The initial source-location audit checked 3,536 locations against 231 distinct pinned Git blobs. Another 240 locations were checked against the two generated source files in the external MSBuild workspace. All paths are relative and all ranges are in bounds. Generator inputs are unchanged between the revisions. The subsequent [deferred-callback and interface review](deferred-callbacks.md) accounts for the current root counts and checks the 30 locations in newly exposed roots.

## Remaining limits and execution evidence

Possible implementation targets are conservative candidates. Event ordering, general receiver flow through guards and assignments, variance constraints, and implicit property execution are not proven by this case. The existing [receiver-flow issue](../issues/receiver-flow.md) remains open for guarded Serilog calls.

The eight reviewed text/Markdown and JSON snapshots cover all four views. A second local run passed all 15 case checks, including byte-identical snapshot comparisons for these four views (`Admin_COLLIN_2026-09-24_19_34_48_net11.0.trx`). Routine discovery selects 13 checks and excludes only the two automatic Autofac views. Cross-platform acceptance remains pending the scheduled/manual all-case workflow; routine CI selects the two focused views.
