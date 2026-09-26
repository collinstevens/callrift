# Development performance inventory

This work tracks `WORKFLOW_DEVEX_GOAL.md`; it does not resume `GOAL.md`.
The inventory describes existing assertions, not permission to remove integration
coverage. Migration is incremental. Unsampled classes have no measured cost rank.

## Evidence and measurement conditions

The starting goal records a Windows 639-case run at 97.32 minutes, including
265 restored-workspace cases at 73.26 minutes. Its raw timing files are absent
from this checkout, so those aggregates cannot support a per-class ranking here.
A later 12-case concurrency sample went from 70.316 to 44.805 seconds. Neither
that sample nor the measurements below establish a full-suite speedup.

Local baseline: `56e12bf`, 2026-09-26, Ubuntu 26.04 x64, Ryzen 9 7945HX
(16 cores / 32 logical CPUs), 43 GiB RAM, mise .NET SDK
`11.0.100-rc.1.26425.128`, Debug, four-class limit. No concurrent broad suite.
The checkout initially had no project build outputs or restore assets. First
successful focused locked restore including SDK initialization took 2.33 seconds;
the NuGet cache was not deliberately emptied. This is not a cold-download claim.
The earlier sandbox-denied restore is excluded. Raw logs, TRX and `/usr/bin/time`
output stay in ignored `artifacts/devex/`.

Bounded selection, runner startup and discovery included; restore/build measured
separately:

```sh
mise exec -- dotnet restore tests/Callrift.Scenarios/Callrift.Scenarios.csproj --locked-mode
mise exec -- dotnet build tests/Callrift.Scenarios/Callrift.Scenarios.csproj --no-restore
mise exec -- dotnet test tests/Callrift.Scenarios/Callrift.Scenarios.csproj --no-build --filter 'FullyQualifiedName~ConstructorEquivalenceTests|FullyQualifiedName~CallbackRenderingTests' --logger trx
```

The 20-case baseline passed in 93.22 seconds of complete invocation time. Fixtures
used new Git directories and workspace caches; global SDK/package caches remained
warm after restore. Class sums below include overlapping execution and must not be
added to predict invocation time.

| Baseline group | Cases | Sum of case durations | Slowest case |
|---|---:|---:|---:|
| Constructor equivalence, workspace | 7 | 78.534 s | 11.603 s |
| Callback rendering, workspace | 3 | 24.522 s | 9.193 s |
| Constructor equivalence, source | 7 | 13.575 s | 2.229 s |
| Callback rendering, source | 3 | 4.601 s | 1.755 s |

First migration (`56e12bf` plus the source changes in this commit): the same
20 cases passed in 69.43 seconds, 25.5% less invocation time in this one comparison.
All source inputs, directions, selections, callback forms, renderers and independent
expectations remain. The original ten source rows now use direct library calls;
the ten workspace rows retain real CLI execution. No snapshots changed. Global
package caches include the baseline run; fixture repository/workspace caches are
fresh for both invocations. This is a bounded sample, not a full-suite result.

| Migrated group | Cases | Sum of case durations | Slowest case |
|---|---:|---:|---:|
| Constructor equivalence, workspace | 7 | 67.863 s | 9.998 s |
| Callback rendering, workspace | 3 | 22.266 s | 8.390 s |
| Constructor equivalence, direct source | 7 | 0.514 s | 0.459 s |
| Callback rendering, direct source | 3 | 0.103 s | 0.081 s |

A separate warm invocation of `--filter Layer=Fast` passed the ten migrated
source cases in 1.50 seconds including startup/discovery (475 ms reported test
duration). This is still only the migrated subset, not a complete fast-tier claim.

The initial focused build took 3.17 seconds; the changed-code incremental build
for the migration took 2.23 seconds, both without restore. Neither is included in
the test invocation figures. The first direct case still pays Roslyn initialization;
subsequent immutable framework references are reused by the existing provider.
Workspace cases explicitly pass `--no-restore` only after the first command succeeds
for both fixed snapshots. Analysis workers still rerun for those CLI checks.

Second migration (parent `ba478c0` plus the catalog changes in this commit), same
machine/SDK/cache policy: all 32 catalog cases passed before and after, using the
same 64 reviewed snapshot files without edits. Complete invocation fell from
52.62 seconds to 11.31 seconds. The changed-code build was 2.77 seconds separately.
The baseline filter was `FullyQualifiedName~ScenarioTests.CallFlow`; the after
filter adds `|FullyQualifiedName~ScenarioTests.CliCallFlow` because the original
rows are partitioned into two methods, with no dropped or duplicated catalog rows.

Twenty-six catalog cases now analyze each source pair once and render the same
result three ways. Six cases (`orders`, `guard`, `top-level`, `depth`,
`tests-included`, `tests-excluded`) still exercise Git and fresh CLI processes for
all three formats, including diagnostics, default/explicit options, depth and test
inclusion. Working-tree/index, invalid selection and all QueryTests remain real
CLI tests. The in-memory snapshot harness retains the historical output envelope
and fixed revision placeholders so no expected snapshot bytes change; that envelope
does not test process exit or Git revision resolution. Those boundaries remain
covered by the CLI rows. New catalog options fail explicitly in the direct path
until their semantics are mapped, rather than silently using defaults.

The expanded `Layer=Fast` selection passed 36 migrated cases in 1.82 seconds of
complete invocation time (772 ms test duration), without a build or restore.
Many existing direct and semantic cases are still outside that selection. This is
progress toward the complete fast tier, not evidence that its final budget or the
80% behavioral coverage target has been met.

Third migration (parent `ef0b199` plus the generic dispatch changes in this commit):
34 constraint cases, two constraint-edit directions and 14 generic invocation
cases now use direct graphs in their source rows. All 50 workspace rows remain
real CLI/MSBuild checks. Source and workspace rows share the unchanged semantic
assertions, including incompatible targets, absent paths, diagnostics, truncation
and closed-context identities. The typed fixture takes library options; it does
not parse or emulate command-line input. Workspace execution still uses the real
CLI and asserts its exit status.

The fixture keeps only its own immutable source pair and analyzed test-inclusion
setting. Reusing it with different test-inclusion options fails explicitly. No
source/project/framework/configuration/reference/generator cache is shared between
cases. Workspace restore tracking contains only the fixed revision IDs in that
fixture's isolated cache, after successful commands. A later diff can still restore
a previously queried revision when the other revision has not yet been restored;
that remaining duplication is not counted as eliminated.

Comparable four-row filter, before and after (same machine, SDK and conditions as
above; warm global packages, fresh per-case repositories and workspace caches):

```text
(FullyQualifiedName~ConstraintDispatchTests&DisplayName~class-value)|(FullyQualifiedName~GenericContextTests&DisplayName~generic-interface-method)
```

All four rows passed: 15.32 seconds before, 12.32 seconds after. Changed-code build
was 1.94 seconds separately. The full currently tagged fast selection passed 86
cases in 2.01 seconds including runner startup/discovery (961 ms test duration).
Twelve additional workspace rows passed, covering ref-like allow/disallow constraints,
required constructors, metadata unmanaged constraints, both constraint-edit directions,
a closed text context and a generic constructor. These remain subset measurements,
not a complete-tier or whole-suite claim.

Fourth migration (parent `35406a1` plus the initialization changes in this commit):
all 27 static-initialization source rows and 21 constructor-initialization source
rows now reuse direct graphs across queries. Static renderer assertions share the
same diff result across JSON, text and Markdown. All 48 workspace rows remain real
CLI tests, including four static cross-project forms and the constructor project
reference case. The unchanged assertions retain negative controls for constants,
type/name inspection, default structs, record-copy initializers and equivalent
constructors, as well as ordering, cycle, symbol/location and changed-call checks.

The four-row local sample (`explicit-method-repeated` in StaticInitializationTests
and `implicit-base` in ConstructorInitializationTests, both modes) passed in 18.79
seconds before and 14.21 seconds after, on the same prepared machine/cache policy.
The after filter includes the renamed Workspace-prefixed methods. The incremental
build took 0.89 seconds separately. Seven additional workspace checks passed: all
four static cross-project forms, the constructor cross-project case, the static
constant negative control and the default-struct constructor negative control.
The expanded 134-row fast selection passed in 2.31 seconds including startup and
discovery, with no build or restore. No full-suite improvement is inferred.

## CI evidence and outstanding failure

The public run page and GitHub connector provide read-only CI access even when
local `gh run list` is denied. Baseline
[run 36244723276](https://github.com/collinstevens/callrift/actions/runs/36244723276),
revision `56e12bf`, completed successfully on Ubuntu and Windows. All 639 scenario
cases also passed on macOS in 56m36s, but its workspace suite failed one of 42 cases:
`WorkspaceTests.Projects`. During its first solution restore NuGet reported that
`A/obj/A.csproj.nuget.g.targets` already existed; subsequent no-restore formats
correctly reported the missing restored-cache marker. The expected snapshot is
unchanged. This predates the migrations; its cause is still unproven and must be
resolved or substantiated by further platform evidence before completion.

```sh
mise run workspaces:focused -- 'FullyQualifiedName~WorkspaceTests.Projects'
```

At inspection on 2026-09-26, all three OS jobs in
[run 36250101113](https://github.com/collinstevens/callrift/actions/runs/36250101113)
for `ef0b199` had built successfully and were still running the scenario suite.
No pending job is counted as passed.

## Recovered full baseline cost ranking

The existing macOS diagnostic artifact from run `36244723276` contains the full
639-case scenario TRX. It is retained locally under ignored `artifacts/devex/`;
no new artifact uploads were added. Runner image `macos-26-arm64`, macOS 26.6.2,
image version `20260907.0351.1`, SDK `11.0.100-rc.1.26425.128`, revision `56e12bf`.
This hosted machine is not comparable to the local Ryzen machine. Restore and build
preceded the measured test command; fixture caches were unique and global package
cache temperatures varied as the run progressed. Case-duration sums include
concurrent work and are cost-ranking evidence, not elapsed suite duration.

| Class | Rows | Sum of case seconds | Slowest row seconds |
|---|---:|---:|---:|
| StaticInitializationTests | 54 | 1467.92 | 65.96 |
| ConstructorInitializationTests | 42 | 1270.66 | 71.44 |
| GenericContextTests | 28 | 878.94 | 61.00 |
| RecordCopyTests | 24 | 873.76 | 89.20 |
| ReceiverContextTests | 46 | 835.22 | 49.63 |
| ConstraintDispatchTests | 72 | 801.07 | 53.70 |
| DepthVisibilityTests | 20 | 490.28 | 52.84 |
| DispatchQueryTests | 10 | 482.16 | 102.74 |
| StaticInitializationDeclarationTests | 14 | 421.34 | 69.56 |
| RecordCopyValidationTests | 20 | 384.82 | 48.90 |
| VarianceCompatibilityTests | 23 | 378.75 | 46.54 |
| CallCollectionTests | 6 | 359.11 | 120.52 |

TRX timestamps show a peak of four overlapping tests. None of the ten
`PartialCloneTests` overlapped another class. `ConstraintDispatchTests` formed a
403.95-second single-class tail after `RecordCopyTests` finished; the exclusive
partial-clone collection then finished the run in about 2.58 seconds. Keep the
current concurrency limit; eliminate orchestration in the ranked classes before
considering additional workers. CPU, aggregate RSS and process-count peaks are not
available in this TRX and must not be inferred from overlap alone.

Already-direct budget tests also require care: two `GenericContextBudgetTests`
rows totaled 38.18 seconds (31.19-second slowest row), and the single
`ReceiverContextBoundaryTests` row took 12.44 seconds on this runner. They exercise
large state-space limits and must stay covered with an explicit stress selection
unless their implementation/fixture cost can be reduced faithfully. Ordinary
`GenericContextBoundaryTests` totaled 0.94 seconds for eleven rows, cancellation
boundaries 0.006 seconds for seven, and precancelled Git boundaries 0.011 seconds
for four. Those are strong candidates to include in the routine fast tier.

## Existing behavior to cheapest faithful layer

Each linked class and its data rows inherit the migration rule in its row. Test
method names identify the existing assertions; snapshot expectations stay under
review and are never regenerated from new results. Most scenario classes currently
repeat source and workspace modes, then often commands and formats inside a case.
“Direct” means real Roslyn parsing/binding plus library graph/query/render calls,
with independently defined expectations and per-case mutable state.

| Existing class | Protected behavior | Migration and retained integration boundary |
|---|---|---|
| [CallCollectionTests](../tests/Callrift.Scenarios/CallCollectionTests.cs) | Nested expression reachability, guard order and locations | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [CallbackRenderingTests](../tests/Callrift.Scenarios/CallbackRenderingTests.cs) | Distinct callback bodies and per-call-site locations in renderers | Source cases now share one analysis across renderers; workspace CLI matrix retained. |
| [CancellationBoundaryTests](../tests/Callrift.Scenarios/CancellationBoundaryTests.cs) | Cancellation after analysis and during traversal | Already direct; measure before adding to the fast tier. Keep budget stress cases separately selectable if needed. |
| [ConstraintDispatchTests](../tests/Callrift.Scenarios/ConstraintDispatchTests.cs) | Constructed implementation constraints and constraint-only edits | Source rows now reuse direct graphs across diff/tree/reach; all workspace rows retain real CLI/MSBuild with per-fixture restored inputs. |
| [ConstructorDiagnosticTests](../tests/Callrift.Scenarios/ConstructorDiagnosticTests.cs) | Ambiguous constructor diagnostics and omitted bodies | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [ConstructorDiscoveryTests](../tests/Callrift.Scenarios/ConstructorDiscoveryTests.cs) | Added/removed default constructors | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [ConstructorEquivalenceTests](../tests/Callrift.Scenarios/ConstructorEquivalenceTests.cs) | Implicit versus explicit constructor equivalence, both directions and root selections | Source cases now share two analyzed graphs; workspace JSON routing and all syntax forms retained. |
| [ConstructorInitializationTests](../tests/Callrift.Scenarios/ConstructorInitializationTests.cs) | Constructor and initializer call ordering across queries | Source rows now reuse direct graphs; all workspace rows retain CLI/MSBuild, including real cross-project binding. Static renderers share a diff result. |
| [DeferredCallbackTests](../tests/Callrift.Scenarios/DeferredCallbackTests.cs) | Deferred callback edges and receiver evaluation order | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DelegateConstructionTests](../tests/Callrift.Scenarios/DelegateConstructionTests.cs) | Delegate construction, possible callbacks and invalid targets | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DelegateCopyTests](../tests/Callrift.Scenarios/DelegateCopyTests.cs) | Delegate copies, factory order and incompatible return types | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DepthReachTests](../tests/Callrift.Scenarios/DepthReachTests.cs) | Complete versus truncated no-path results | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DepthReachabilityTests](../tests/Callrift.Scenarios/DepthReachabilityTests.cs) | Cycle entry reachability and bounded traversal | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DepthVisibilityTests](../tests/Callrift.Scenarios/DepthVisibilityTests.cs) | Depth omissions, visible leaves and changed bodies | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DispatchCardinalityTests](../tests/Callrift.Scenarios/DispatchCardinalityTests.cs) | Contract identity across implementation cardinality changes | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DispatchCycleIdentityTests](../tests/Callrift.Scenarios/DispatchCycleIdentityTests.cs) | Recursive dispatch and ancestor references | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [DispatchQueryTests](../tests/Callrift.Scenarios/DispatchQueryTests.cs) | Contract signature/root selection and possible implementations | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [FileLocalIdentityTests](../tests/Callrift.Scenarios/FileLocalIdentityTests.cs) | File-local binding and distinct callers | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [GenericContextBoundaryTests](../tests/Callrift.Scenarios/GenericContextBoundaryTests.cs) | Context recursion, limits, signatures, selection and rendering | Already direct; measure before adding to the fast tier. Keep budget stress cases separately selectable if needed. |
| [GenericContextBudgetTests](../tests/Callrift.Scenarios/GenericContextBudgetTests.cs) | Context-budget boundaries without invented changes | Already direct; measure before adding to the fast tier. Keep budget stress cases separately selectable if needed. |
| [GenericContextTests](../tests/Callrift.Scenarios/GenericContextTests.cs) | Invocation type arguments and downstream dispatch | Source rows now reuse direct graphs across diff/tree/reach; all workspace rows retain real CLI/MSBuild with per-fixture restored inputs. |
| [GenericDispatchTests](../tests/Callrift.Scenarios/GenericDispatchTests.cs) | Invariant/variant contracts and repeated type parameter unification | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [GitCancellationTests](../tests/Callrift.Scenarios/GitCancellationTests.cs) | Precancelled Git operations avoid inaccessible repositories | Already direct; measure before adding to the fast tier. Keep budget stress cases separately selectable if needed. |
| [InterfaceReceiverTests](../tests/Callrift.Scenarios/InterfaceReceiverTests.cs) | Interface receiver constraints and abstract class implementations | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [PartialCloneTests](../tests/Callrift.Scenarios/PartialCloneTests.cs) | Git object hydration, ordering, errors and cancellation | Keep real repositories, fetches and child processes; retain exclusive environment collection. |
| [ProjectClassificationTests](../tests/Callrift.Scenarios/ProjectClassificationTests.cs) | Literal metadata and conditional test-project classification | Keep metadata inference in memory; retain MSBuild classification and shared Directory.Build.props wiring. |
| [QueryTests](../tests/Callrift.Scenarios/QueryTests.cs) | Tree/reach selection, depth/path limits, cycles, strict/exit-code behavior, merge base and invalid arguments | Move graph traversal to direct queries; keep CLI status/diagnostics, argument errors, merge base and a three-format matrix. |
| [ReceiverConstraintTests](../tests/Callrift.Scenarios/ReceiverConstraintTests.cs) | Reference casts, conversions, sibling overrides and generic substitutions | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [ReceiverContextBoundaryTests](../tests/Callrift.Scenarios/ReceiverContextBoundaryTests.cs) | Receiver-state budget preserves unchanged callers | Already direct; measure before adding to the fast tier. Keep budget stress cases separately selectable if needed. |
| [ReceiverContextDelegateTests](../tests/Callrift.Scenarios/ReceiverContextDelegateTests.cs) | Separate delegate receivers and inherited implementations | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [ReceiverContextFileLocalTests](../tests/Callrift.Scenarios/ReceiverContextFileLocalTests.cs) | Closed file-local contexts, revision materialization and same-basename paths | Direct semantics can move; retain actual revision materialization and path/assembly identity integration. |
| [ReceiverContextTests](../tests/Callrift.Scenarios/ReceiverContextTests.cs) | Inherited receiver context, cycles and unrelated sibling edits | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [RecordCopyPresentationTests](../tests/Callrift.Scenarios/RecordCopyPresentationTests.cs) | Clone labels, symbol identities, locations and copy order | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [RecordCopyTests](../tests/Callrift.Scenarios/RecordCopyTests.cs) | Record copy construction, expansion and ambiguous declarations | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [RecordCopyValidationTests](../tests/Callrift.Scenarios/RecordCopyValidationTests.cs) | Invalid record diagnostics and unavailable bodies | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [ScenarioTests](../tests/Callrift.Scenarios/ScenarioTests.cs) | Reviewed semantic and renderer snapshots; index/working tree and invalid selection | 26 catalog rows now use shared in-memory analysis; six retain CLI format routing. Selection errors and index/working-tree snapshots stay integration. |
| [StaticCallbackInitializationTests](../tests/Callrift.Scenarios/StaticCallbackInitializationTests.cs) | Independent callback initialization state | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [StaticCoalesceInitializationTests](../tests/Callrift.Scenarios/StaticCoalesceInitializationTests.cs) | Conditional coalescing initialization and single receiver evaluation | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [StaticEventOrderTests](../tests/Callrift.Scenarios/StaticEventOrderTests.cs) | Handler evaluation before static initialization | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [StaticInitializationDeclarationTests](../tests/Callrift.Scenarios/StaticInitializationDeclarationTests.cs) | Unavailable initializer bodies and invalid declarations | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [StaticInitializationDepthTests](../tests/Callrift.Scenarios/StaticInitializationDepthTests.cs) | Completed initialization and later change markers | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [StaticInitializationIdentityTests](../tests/Callrift.Scenarios/StaticInitializationIdentityTests.cs) | Partial initializer ordering and declaring-project identity | Direct within-part order can move; retain separate projects with identical assembly/type names. |
| [StaticInitializationTests](../tests/Callrift.Scenarios/StaticInitializationTests.cs) | Conditional initialization and declaring-project links | Source rows now reuse direct graphs; all workspace rows retain CLI/MSBuild, including real cross-project binding. Static renderers share a diff result. |
| [SyntaxIdentityTests](../tests/Callrift.Scenarios/SyntaxIdentityTests.cs) | Formatting-insensitive identity versus significant literal whitespace | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [TestAssemblyClassificationTests](../tests/Callrift.Scenarios/TestAssemblyClassificationTests.cs) | Test assembly reference recognition and conditions | Move classification to snapshots; retain an actual evaluated test/application workspace boundary. |
| [TopLevelConstructorTests](../tests/Callrift.Scenarios/TopLevelConstructorTests.cs) | Partial top-level Program constructor binding | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [VarianceCompatibilityTests](../tests/Callrift.Scenarios/VarianceCompatibilityTests.cs) | Variant compatibility, inheritance and nested variance | Move compatibility matrices to real minimal compilations; retain separate assembly identity checks. |
| [VirtualDispatchTests](../tests/Callrift.Scenarios/VirtualDispatchTests.cs) | Overrides, direct base calls, exact receivers and method groups | Move semantic rows and negative controls to direct calls. Keep representative source/MSBuild CLI wiring; any project-specific row stays integration. |
| [XunitExecutableClassificationTests](../tests/Callrift.Scenarios/XunitExecutableClassificationTests.cs) | Executable test projects versus application executables | Retain evaluated package/project classification integration. |

| Other suite | Protected behavior | Cheapest faithful layer and required boundary |
|---|---|---|
| [WorkspaceTests](../tests/Callrift.Workspaces/WorkspaceTests.cs) | Parity, packages, defines, project references, generators, framework selection and restored-cache failures | Integration for evaluation, restore, generators and references. Analyze once for renderer snapshots; preserve missing-cache and framework error cases. |
| [FrameworkDispatchTests](../tests/Callrift.Workspaces/FrameworkDispatchTests.cs) | Framework interfaces and referenced implementations | Real multi-project/framework integration. |
| [GeneratedInitializerIdentityTests](../tests/Callrift.Workspaces/GeneratedInitializerIdentityTests.cs) | Generated initializer identities and source edits | Real generator integration; reuse a result across formats only, never across generator inputs. |
| [InterceptorTests](../tests/Callrift.Workspaces/InterceptorTests.cs) | Interceptor replacement, inserted calls and repeatable generated identities | Real generator integration and fresh-process determinism. |
| [SweepProcessTests](../tests/Callrift.RealWorldCases/SweepProcessTests.cs) | Abrupt exit, bounded output, deadlines and cancellation of child processes | Bounded process integration; these require real child lifetimes. |
| [RealWorldCaseTests](../tests/Callrift.RealWorldCases/RealWorldCaseTests.cs) | Every manifest case/view and restored Serilog | Explicit broad E2E, plus bounded routine CI views. Preserve pinned revisions, reviewed snapshots, both modes and platform coverage. |
| [Package-Smoke.ps1](../scripts/Package-Smoke.ps1) | Installed tool and dnx execution | Packaging integration; cannot be replaced with direct library calls. |

## Migration order and isolation

1. Constructor equivalence and callback rendering have a comparable local baseline.
   Move their source rows to direct graphs; share analysis across direction,
   selection and renderer checks. Preserve all ten workspace rows and CLI assertions.
   Reuse restored fixture inputs after the first successful command, with the
   fixture's unique cache and unchanged revisions/project/options. No global cache
   or production behavior change is needed.
2. Migrate the scenario catalog and semantic families above with their existing
   negative controls. Compare each old expectation with the direct result before
   retiring permutations. Keep query argument/status/diagnostic tests in the CLI
   layer and retain fresh-process format/determinism coverage.
3. Separate pure test-project metadata inference from evaluated project behavior.
   Preserve multi-project, same-assembly, generated, framework, cancellation,
   cache failure/invalidation, isolation and process tests at their real boundaries.
4. Tag and measure the complete fast selection, including discovery and startup,
   then expose validated fast/integration/E2E commands and independent CI jobs.
   The current 134 migrated source rows alone do not constitute the fast tier.

`PartialCloneTests` mutates `GIT_TRACE2_EVENT` and `GIT_NO_LAZY_FETCH`; it remains
in `ProcessEnvironmentCollection` with parallelization disabled. Other fixture
cache paths are passed through child environments, not process-wide mutation.
Keep four concurrent classes. The baseline's `/usr/bin/time` maximum RSS measures
a process high-water mark, not simultaneous process-tree memory. A single process
sample observed eight dotnet processes; it does not establish peak count. CPU,
aggregate memory, process-count sampling and the remaining serial tail still need
measurement before tuning concurrency. Child restore/MSBuild work already overlaps.

No fast hook has been added. Full-tier latency, changed/unchanged hook p95,
supported-platform CI evidence, broad migration share and a full before/after
comparison remain unproven. Existing formatting/message gates and asynchronous
E2E policy remain in force.
