# Development performance inventory

This work tracks `WORKFLOW_DEVEX_GOAL.md`; it does not resume `GOAL.md`.
The first phase completed at `90083f5`; the second phase's broad-suite performance
goal remains active. The latest completed evidence includes the
[matched full-corpus cold/warm comparison](#matched-complete-e2e-results-at-2fae0fb)
and [cold shard confirmation](#cold-confirmation-at-3060e3e). Warm repository
caches are confirmed, but Windows workspace/case budgets and combined suite
timing under staggered job starts remain open.

The inventory describes existing assertions, not permission to remove integration
coverage. Historical checkpoint observations are retained below. Unsampled
classes have no measured cost rank; first-phase completion does not establish
completion of the active second phase.

## First-phase results

| Evidence | Result |
|---|---|
| Latest prepared fast command, `9e7a117` | 350 passed in 4.58 s including changed-code build, mise and PowerShell |
| Matched complete scenario runs | The same 655 names passed in 460.06 s with default ordering and 448.36 s with prioritized ordering |
| Fresh checkout, warm SDK/package caches | Explicit restore 1.07 s; installed commit hooks 3.79 s; first fast command and build 4.76 s |
| Supported-platform feedback, `90083f5` | Fast, representative integration/packaging, quality and all three broad verification jobs passed |
| Preservation | 706 executions per OS passed: 350 fast, 305 slow scenarios, 26 workspaces and 25 routine cases; reviewed snapshots and production code unchanged from `56e12bf` |

The matched scenario comparison is one observation, not a full E2E speedup claim.
Detailed cache conditions, resource measurements and coverage accounting follow.

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

## Record-copy and receiver migration

Parent `a23d291`, same local machine, SDK and cache policy: 47 existing source
rows now reuse direct graphs across tree, reach, focused/unfocused diff and format
assertions. Their 45 workspace counterparts retain real CLI/MSBuild execution,
including the `markdown` alias, invalid-declaration controls, cross-project record
copies, exact identities/locations, receiver conversions, cycles and sibling edits.
The two ambiguous-copy rows were source-only before migration. No snapshots changed.

The matched four-row filter below passed before and after. Complete no-build
invocation fell from 21.84 to 15.71 seconds. Each invocation used fresh fixture
repositories/caches and warm global SDK/package caches. This sample is not a full
suite speedup.

```sh
mise run test:focused '(FullyQualifiedName~RecordCopyTests&DisplayName~sealed-copy)|(FullyQualifiedName~ReceiverContextTests&DisplayName~field-boundary)'
```

The everyday `mise run test:fast` passed 203 rows in 3.92 seconds including mise,
PowerShell, discovery and the changed-code incremental build (3.69 seconds inside
dotnet). Discovery is still 639 total: 203 fast plus 436 non-fast, with no overlap.
Eight focused workspace rows also passed in 38.25 seconds of dotnet invocation,
covering both cross-project record forms, presentation, private malformed copies,
receiver conversion, cycle references and unaffected siblings.
The final broad semantic migration is incomplete; 203 rows are not claimed to meet
the goal's 80% meaningful-behavior target.

## Reusing real workspace graphs

Parent `5a36e76`: the eight migrated semantic families now use `WorkspaceFixture`
for 141 workspace rows. Each fixture has unique before/after directories, restores
each immutable project snapshot once, and starts the existing real MSBuild worker
once per snapshot. Queries and renderers then consume the serialized graphs in
process. Project evaluation, references, metadata, compilation and worker serialization
remain real. Temporary inputs are deleted before queries run; no graph or restored
input is reused across tests, revisions or changed project/configuration inputs.
There is no persistent worker, shared mutable workspace or production change.

Two rows explicitly retain `AnalysisFixture.CreateWorkspaceCliAsync`: sealed record
copying exercises the real `markdown` alias, and record presentation exercises
all three commands/formats, IDs, locations and order. Constructor equivalence,
callback rendering, the six CLI catalog cases and the dedicated workspace/cache,
generator, path materialization, cancellation and CLI-error suites also retain their
existing process boundaries. This migration changes no test discovery or expected
assertions. The helper's fixed target/configuration is only for its existing semantic
fixtures; it does not replace framework selection or cache invalidation tests.

The same eight-row record/receiver boundary command passed before and after this
helper change: 38.25 to 25.05 seconds including dotnet startup and incremental build,
with fresh fixture inputs and warm global SDK/package caches. Seven additional
workspace rows passed in 29.41 seconds, covering constructor project references,
three metadata constraint controls, both constraint-edit directions and nested generic
substitution. Five further rows passed in 27.43 seconds: all four static initializer
project-link forms plus the retained sealed-copy CLI alias check. The fast tier
still passed all 203 rows (3.51 seconds of dotnet invocation). These bounded checks
do not establish full-suite or cross-OS throughput.

## Depth, dispatch selection and variance migration

Parent `83cfc0e`, same local machine/SDK/cache policy: 36 source rows in
DepthVisibility, DepthReach, DepthReachability, DispatchQuery,
StaticInitializationDeclaration and VarianceCompatibility now run directly.
Thirty workspace semantic rows reuse real MSBuild graphs, retaining depth omission,
absent-path, cycle, dispatch cardinality, signature identity and variance controls,
including the assembly named `array`. The separate concurrent-expansion stress
case remains outside Fast. All 639 rows remain discoverable: 239 Fast, 400 non-Fast,
with no overlap and no snapshot changes.

Seven static-declaration workspace rows retain real CLI execution because their
text/Markdown assertions include diagnostic codes emitted on stderr. Direct source
rows assert these same diagnostic codes in JSON and share a query result across
renderers; they do not manufacture stderr. The CLI rows restore their unchanged
snapshot once, then pass `--no-restore` for later commands.

A matched four-row sample (depth `source-child` plus selected-contract signature,
source and workspace in each) passed before and after. Complete no-build invocation
fell from 20.26 to 6.77 seconds with fresh fixture inputs and warm global package
caches. The updated everyday fast command passed 239 rows in 3.80 seconds including
mise, PowerShell and incremental build (3.57 seconds inside dotnet). These are
bounded measurements; the final fast-layer scope is still incomplete. All 37
workspace rows in these six classes passed in 87.50 seconds of dotnet invocation,
including the retained diagnostic stderr matrix.

## Project classification migration

Parent `c7362f8`: five literal/conditional metadata rows, nine assembly-reference
rows and two source shared-settings rows now analyze real source snapshots directly.
Assembly-reference cases analyze inclusion and exclusion separately, then reuse each
result across all three renderers. Their existing application/test, condition,
explicit-override and inference controls are unchanged. The two evaluated solution
rows retain the real four-project .NET 10 CLI/MSBuild fixture with shared targets,
package references and both test-inclusion modes. They restore once before their
first format, then use the existing isolated cache without restoring again.

The everyday fast command passed 255 rows in 4.19 seconds including mise, PowerShell
and changed-code incremental build (3.94 seconds inside dotnet). Discovery remains
639: 255 Fast plus 384 non-Fast, no overlap. No snapshot or production code changed.
Both retained solution rows passed in 31.11 seconds of dotnet invocation.
No full-suite speedup or completed coverage-percentage claim follows from this count.

## Callback state, ordering and syntax identity migration

Parent `2ae1fee`: 19 source rows across StaticCoalesceInitialization,
StaticCallbackInitialization, StaticEventOrder, StaticInitializationDepth,
ReceiverContextDelegate, CallCollection and SyntaxIdentity now use direct graphs.
Their 19 workspace counterparts reuse real MSBuild graphs. Conditional initialization,
single receiver/index evaluation, independent callback state, handler order, completed
initialization, inherited delegate targets, exact call-site lines and significant
literal whitespace keep their existing assertions. Multi-format checks share results;
the .NET 10 syntax fixtures still use their original framework in workspace mode.

The everyday fast command passed 274 cases in 4.25 seconds including mise, PowerShell
and changed-code build (4.02 seconds inside dotnet). Discovery is unchanged at 639:
274 Fast plus 365 non-Fast, no overlap. These are scope and invocation measurements,
not a complete suite speedup or final coverage claim. All 19 workspace counterparts
passed in 46.60 seconds of complete dotnet invocation.

## Dispatch target and receiver migration

Parent `93e0889`: 28 source rows from GenericDispatch, InterfaceReceiver,
ReceiverConstraint, VirtualDispatch, DispatchCardinality and DispatchCycleIdentity
now use direct analysis. Their 28 workspace counterparts reuse real analyzed graphs.
Forward/reverse target-set changes, preserved identities, ancestor references,
reference conversions, incompatible/compatible generic controls, receiver exclusions,
direct base method groups and source locations retain their assertions. The generic
override case still evaluates its separate Contracts project in workspace mode.

The everyday fast command passed 302 cases in 4.24 seconds including mise,
PowerShell and changed-code build (4.00 seconds inside dotnet). Discovery is still
639 cases: 302 Fast, 337 non-Fast, no overlap. No snapshots or production code changed.
All 28 workspace counterparts passed in 67.72 seconds of dotnet invocation.
At this checkpoint, fast-tier scope was not yet final; remaining semantic candidates
and real boundaries still needed separation before assessing the coverage target.

## Constructor, delegate and file-local identity migration

Parent `2307a4b`: 16 constructor/discovery, top-level Program and delegate source
rows now reuse direct graphs; their 16 workspace counterparts reuse real MSBuild
graphs. Deferred execution, receiver/factory ordering, unknown targets, incompatible
return types and ambiguous constructor diagnostics retain their existing assertions.
Renderer permutations reuse the same query result. All 16 workspace rows passed
in 36.23 seconds including dotnet startup and incremental build checks.

Ten identity rows join the fast selection: five already-direct physical-path checks,
two file-local caller cases, the same-basename receiver case and two partial-order
cases. Actual revision materialization remains in both source and workspace modes.
Workspace file-local callers, same-basename paths, compile-item ordering and identical
assembly names in separate projects retain real CLI execution. Repeated commands
on the same restored revision use `--no-restore`; source-only rendering shares results.
The partial-order diagnostic code remains asserted in JSON and actual CLI stderr.
All 17 retained identity integration rows passed in 34.81 seconds including dotnet
startup and incremental build checks.

The everyday fast command passed all 328 tagged cases in 4.39 seconds including
mise, PowerShell and changed-code build (4.08 seconds inside dotnet), on the same
prepared local machine and SDK described above. Discovery remains 639 cases:
328 Fast and 311 non-Fast, with no overlap. No snapshots or production code changed.
This count is not a claim that 80% of distinct behavioral cases have been audited.

The preceding checkpoint `2307a4b` passed fast and representative integration jobs
on Linux, Windows and macOS, plus quality checks in
[CI run 36252978048](https://github.com/collinstevens/callrift/actions/runs/36252978048).
Its broad verification jobs were still running when checked; they are not reported
as passed.

## Final layer audit

The final audit found two families omitted from the earlier source migrations.
`GeneratedInitializerIdentityTests` constructed real Roslyn compilations directly,
including interceptor metadata and graph serialization, despite living in the
workspace project. Its 16 existing rows move to the scenario project's fast tier;
actual SDK/project-reference generators remain in the workspace integration suite.
The six executable xUnit package-name rows only inspect literal source project
metadata. They now analyze once per test-inclusion setting and reuse each diff
across three renderers. Neither family loses expectations or gains new test rows.

Framework dispatch retains all four actual mixed-framework CLI cases. Their first
diff restores both fixed revisions; subsequent tree and reach commands reuse those
restored inputs with `--no-restore`. Framework/project identities and fresh child
processes remain asserted. This does not cache across changed project inputs.
All four framework-dispatch rows passed locally with the unchanged assertions.
The pinned Serilog MSBuild format matrix likewise restores on its first invocation
and uses `--no-restore` for the remaining two formats. Its existing reviewed
snapshot passed locally without edits.

The coverage accounting treats execution permutations separately from behaviors:
257 workspace scenario rows repeat source semantic expectations, and eight of the
16 generated-initializer rows repeat the same inputs after graph serialization.
Those permutations remain executable, but each pair counts once for the design
signal. All other integration cases count separately, even where they also overlap
fast semantics. The 26 remaining workspace rows and four sweep-process boundary
cases are included. Pinned real-world repository/view permutations remain a separate
E2E scale/corpus dimension; they are not claimed as migrated fast cases.

After those explicit deductions, 342 of 420 behavior rows (81.4%) have fast
coverage: 350 fast executions minus eight serialization permutations. The denominator
is 655 scenario executions plus 26 workspace and four process checks, minus 257
workspace semantic permutations and eight serialization permutations. This is a
reviewable design signal, not line/branch coverage or a claim that integration
wiring runs in process. Counting every execution instead gives 350 of 685 (51.1%)
for these projects; none of the slower rows is removed to improve the ratio.

Remaining slow scenario behavior beyond those 257 workspace counterparts consists
of four expansion/budget stress rows, ten partial-clone rows, two evaluated project
classification rows, eleven CLI query/status/merge-base rows, ten real file-local
revision-materialization rows, eight catalog/index/invalid-selection CLI rows,
two same-assembly project-identity rows and one workspace array-compatibility row.
All are deliberate integration or stress coverage. Ordinary semantics have a fast
path, while costly inputs and actual I/O boundaries remain explicitly selectable.

## Complete local measurement

At compiled revision `527ef15`, all 639 scenarios passed in 575.29 seconds
(9m35s) including mise/dotnet startup and discovery, with `--no-build`. This run
preceded relocation of the 16 generated-initializer cases and migration of the six
executable-classification rows. It covered every scenario migration through that
checkpoint. Fresh per-case repositories/workspaces and warm global SDK/package
caches were used, on the local machine documented above; no other local build or
test suite overlapped. There is no same-machine full pre-migration run, so this
measurement does not establish a whole-suite speedup against the historical Windows
or macOS baselines. The earlier matched local samples remain the before/after evidence.

Process-tree sampling every 250 ms observed 18 processes at peak and 2,845.80 MiB
of aggregate RSS. Summing RSS counts shared pages more than once and is not private
memory. Observed CPU time was at least 1,836.77 seconds; processes that started and
exited between samples may be missing, so this is a lower bound. TRX intervals show
at most four concurrent classes and zero partial-clone overlap with any other class.
The slowest regular class, ConstraintDispatchTests, ran alone for a 158.00-second
final tail before the exclusive partial-clone collection. Its summed test duration
was 197.05 seconds; StaticInitializationTests followed at 164.13 seconds and
ReceiverContextTests at 134.05 seconds. Class scheduling is the next measured
optimization candidate. The four-class limit remains the bounded default; this
run does not justify increasing it for smaller hosted runners. Raw samples and TRX
stay in ignored artifacts, with no new uploads.

After the final relocation/classification edits, all 350 fast cases passed in
4.68 seconds through `mise run test:fast`, including PowerShell startup and the
changed-code build (4.37 seconds inside dotnet). A separate unchanged-code
`mise run test` invocation passed in 4.21 seconds (3.92 inside dotnet), with no
other build or test process overlapping. An earlier warm sample overlapping discovery
was excluded. These are prepared-checkout measurements; initial restore and build
costs and the existing ten-sample hook experiment are recorded separately above.
No claim of an empty package cache or a cold-download budget is made. Of the 350
fast cases, 345 completed within 100 ms; the median was 11.76 ms, p95 35.19 ms and
maximum 437.08 ms, including cases that pay initial Roslyn setup.

Discovery after relocation is 655 scenario rows: 350 Fast and 305 non-Fast with
no overlap. The 16 added to the scenario project are the same rows removed from
the workspace project; the total across those projects remains 681. Existing
reviewed snapshots are unchanged throughout the workstream.

## Schedule expensive collections first

The complete local run showed ConstraintDispatchTests starting 376.41 seconds
after the first scenario and finishing at 573.48 seconds. It ran alone for the
last 158.00 seconds before the exclusive partial-clone collection. The collection
orderer now prioritizes the twelve classes with more than 50 seconds of summed
time in that run, in descending measured order. The remaining collections use a
stable ordinal name order. Priority is only a scheduling hint: every supplied
collection is returned, the four-class execution limit is unchanged, and xUnit
still owns the exclusive collection's nonparallel execution.

The orderer's collection names follow the pinned xUnit 2.9.3
[collection-per-class factory](https://github.com/xunit/xunit/blob/v2-2.9.3/src/xunit.execution/Sdk/Frameworks/CollectionPerClassTestCollectionFactory.cs).
Unknown/new collections remain included. Refresh the measured priority list if
future timing evidence changes which classes dominate; it is not a test filter
or a permanent claim about class cost.

All 350 fast cases passed with the orderer in 4.58 seconds including the changed-code
build and full mise/PowerShell invocation (4.29 seconds inside dotnet). TRX confirms
that the four largest classes were the first four scheduled. Four representative
workspace rows plus all ten partial-clone rows passed in 8.38 seconds of dotnet
invocation; their TRX also confirms at most four concurrent classes and no
partial-clone overlap. The matched full comparison below now supplies the
scheduling measurement.

## Matched scheduling measurement and fresh-checkout costs

Both complete scenario runs executed exactly the same 655 test names and passed
all of them. `2be5272` uses xUnit's default ordering; `9e7a117` adds only the
collection orderer and its documentation. Both used `--no-build`, the documented
local machine/SDK, warm global SDK/package caches and fresh per-case repositories
and workspaces. No other local build or test suite overlapped either measurement.
The prioritized run executed first; the default-order run followed in a temporary
detached checkout, with its build measured separately. The temporary checkout
created no commits and was removed after preserving its local TRX and profiling data.

The explicit full-scenario diagnostic command was:

```sh
mise exec -- dotnet test tests/Callrift.Scenarios/Callrift.Scenarios.csproj --no-build --logger trx
```

| Full scenario measurement | Default, `2be5272` | Prioritized, `9e7a117` |
|---|---:|---:|
| Cases passed | 655 | 655 |
| Complete invocation | 460.06 s | 448.36 s |
| Final regular-class serial tail | 12.22 s | 4.23 s |
| Maximum concurrent classes | 4 | 4 |
| Partial-clone overlap with other classes | 0 | 0 |
| Observed peak process count | 18 | 18 |
| Sampled aggregate peak RSS | 2,802.64 MiB | 3,160.69 MiB |
| Observed CPU time, lower bound | 1,839.38 s | 1,843.32 s |

Prioritization reduced invocation time by 11.70 seconds (2.5%) in this comparison,
with higher peak aggregate RSS. The 250 ms sampling and shared-page limitations
above apply. The earlier 639-case run's 158-second tail did not recur in the newer
default-order sample; its 575.29-second duration must not be used to attribute a
larger gain solely to scheduling. The retained policy starts the measured expensive
classes first while keeping the same bounded concurrency. These two runs do not
establish a confidence interval or a complete E2E-suite speedup.

Before the default-order run, the temporary checkout also measured first use at
`9e7a117`. A staged private-field rename exercised the actual installed formatting
hook and was discarded afterward. The test assembly did not exist before the first
fast command. Global SDK/package caches were warm; SDK downloads, empty-package-cache
downloads and OS cache eviction were outside this measurement.

| Fresh-checkout phase | Complete invocation |
|---|---:|
| Explicit locked solution restore | 1.07 s |
| Installed pre-commit hook | 3.76 s |
| Installed conventional-message hook | 0.02 s |
| Both hooks together | 3.79 s |
| First `mise run test:fast`, including first build | 4.76 s; all 350 passed |

The hooks ran no restore, build or tests. The first fast invocation reported
4.50 seconds inside dotnet; its 4.76-second total includes mise and PowerShell.
This separates checkout preparation and hook latency from the routine test budget.
The existing five changed-code and five non-code warm hook samples remain the p95
sample; this single fresh-checkout result is not a p95 estimate.

## Supported-platform feedback before fixture repair

[Run 36254402575](https://github.com/collinstevens/callrift/actions/runs/36254402575)
for `9e7a117` passed all 350 fast cases, representative integration and packaging on
Ubuntu 24.04, Windows 2025 VS2026 and macOS 26 arm64, plus quality checks. Restore
and build preceded the fast command. Approximate script time runs from the Actions
step start through the wrapper's result summary and includes PowerShell startup.
The job logs retain their reported working-tree-modified marker after setup.

| Hosted image | Fast dotnet invocation | Complete fast script | Separate build | Representative integration job |
|---|---:|---:|---:|---:|
| Ubuntu 24.04 | 9.45 s | 11.37 s | 9.42 s | 2m35s |
| Windows 2025 VS2026 | 10.49 s | 13.04 s | 9.54 s | 3m54s |
| macOS 26 arm64 | 8.57 s | 12.19 s | 8.19 s | 2m38s |

Hosted first-invocation timings differ from the prepared local development budget;
the table does not claim a sub-ten-second total on every CI host. Representative
job times include tool setup, builds, 12 real Git/CLI cases, four workspace cases
and installed-package smoke checks; queue time is excluded. All are below the
ten-minute feedback target. Broad macOS verification subsequently passed all 305
slow scenarios but failed one of 26 workspace cases; the other two broad jobs
were subsequently cancelled by the fixture-repair checkpoint. They are not counted
as passed. The replacement run is described below. Documentation-only evidence
checkpoints use
`[skip ci]` to preserve ongoing validation of unchanged source rather than cancel
it with an identical run.

## Resolve physical fixture directories

The macOS `WorkspaceTests.Projects` failure in `9e7a117` repeats the baseline
failure: NuGet reports an existing generated `A.csproj.nuget.g.props` file during
solution restore. The failed path begins with `/private` while the fixture root
uses its temporary-directory alias. The other 25 workspace cases passed; real-world
cases were skipped after the workspace failure.

A temporary Linux reproduction used the same three-project solution shape and SDK,
with one project referenced both by the solution and another project. Restoring
through a symlinked root failed in two of three fresh-obj attempts with the same
generated-file collision. The successful alias attempt's restore graph included
both alias and physical paths for project A. All three physical-root controls
passed and contained only the three distinct physical project paths. This exposes
duplicate restore identities for one physical project, rather than a snapshot or
semantic-analysis discrepancy. Diagnostic logs remain in ignored `artifacts/devex`.

Fixture paths now resolve links in the temporary directory and its ancestors before
allocating unique repository, workspace-cache and analyzed-workspace directories.
The helper is shared by the scenario and workspace assemblies, uses no child
processes, and preserves per-fixture isolation. Production path handling and
reviewed expectations are unchanged; explicitly symlinked production cache paths
are outside this harness correction.

On the local Ubuntu machine, the existing Projects, Generator and restored-cache/
framework cases passed with `TMPDIR` pointing below a symlinked ancestor: three
cases, 28.24 seconds including dotnet startup/build/restore, 28.55 seconds including
mise and PowerShell. Five existing workspace file-local and syntax-identity cases
also passed below a symlinked ancestor, exercising both real CLI materialization
and direct workspace graph reuse: 30.60 seconds for dotnet, 30.92 seconds overall.
All 350 fast cases passed afterward in 4.01 seconds for the dotnet invocation.

[Run 36256895515](https://github.com/collinstevens/callrift/actions/runs/36256895515)
for `d88af5a` passed all three fast jobs, quality, and representative integration/
packaging on macOS and Ubuntu. The macOS selection includes the previously failing
Projects case. Windows exposed a helper regression before fixture creation:
`ResolveLinkTarget` on the drive root throws `DirectoryNotFoundException` for
`C:\`. All 12 representative Git/CLI cases and 301 of 305 slow scenarios failed
there; the four remaining direct stress cases passed. Those failures are not
semantic regressions or successful broad validation.

The helper now returns filesystem roots before attempting link resolution and
continues resolving links in their descendants. This preserves drive/share roots
and the physical-path correction without suppressing filesystem errors. Under the
nested symlink `TMPDIR`, all 17 existing Git/CLI, partial-clone, file-local and
syntax-identity cases passed in 31.71 seconds overall; the three Projects,
Generator and cache/framework cases passed in 26.26 seconds overall.

[Run 36257230296](https://github.com/collinstevens/callrift/actions/runs/36257230296)
for `90083f5` passed all 350 fast cases on each supported OS, all three
representative integration/packaging jobs, and quality. In particular, Windows
passed the 12 real Git/CLI cases that previously failed at fixture creation and
all four workspace cases; macOS also passed Projects again. Fast dotnet invocations
took 9.41 seconds on Ubuntu, 7.36 on Windows and 7.05 on macOS after separate
restore/build steps. These figures exclude PowerShell/mise startup. All three
broad verification jobs subsequently completed successfully.

## Final CI and completion evidence

Final [run 36257230296](https://github.com/collinstevens/callrift/actions/runs/36257230296)
for source `90083f5` completed successfully. Every OS passed 350 fast cases, 305 slow
scenarios, 26 workspace cases and 25 routine real-world/process cases, with no
failures or skips. This preserves the earlier 706-execution total while separating
routine semantic feedback from actual Git, CLI, restore, generator and corpus
boundaries. Representative integration/packaging and quality also passed.

| Hosted platform | Slow scenarios, dotnet seconds | Workspaces, dotnet seconds | Routine cases, dotnet seconds | Whole broad job, seconds |
|---|---:|---:|---:|---:|
| Ubuntu | 1613.21 | 528.55 | 722.93 | 2912.83 |
| Windows | 1358.82 | 545.57 | 942.85 | 2941.81 |
| macOS | 1343.73 | 531.37 | 842.03 | 2773.51 |

Broad tests used the pinned SDK and Debug builds, with restore/build preceding
`-NoBuild`. Whole-job spans use first/last log timestamps and include setup;
queue time is excluded. The logs retain their working-tree-modified marker.
These hosted observations are not matched local speedup measurements. All fast
and representative results arrived independently before the broad jobs finished;
representative job spans were 162.92 seconds on Ubuntu, 229.01 on Windows and
116.05 on macOS. Broad suites remain absent from local commit and push gates.

The completion audit checked the behavior inventory and preserved expectations,
fixture-local graph/renderer reuse, real integration boundaries, matched 655-name
local measurements, four-class/exclusive-collection timing evidence, fast command
and hook budgets, invalid-selection failures, contributor/agent commands and CI
failure diagnostics. Production code, reviewed snapshots, the pinned case manifest
and release workflow are unchanged from `56e12bf`. Scheduled/manual all-case and
history-sweep coverage remains available; this run verifies the routine corpus,
not a new execution of every scheduled or release workflow. No test hook, new test
case or external artifact destination was added. The implementation goal remains
paused.

## Explicit feedback commands and hook sample

The command/workflow checkpoint (parent `c94cdb5` plus the changes in this commit)
adds the already-direct cancellation, precancelled Git and ordinary generic-context
boundary classes to the fast selection. Discovery proves 156 fast rows plus 483
non-fast rows equals the original 639, with no overlap. The three costly context
budget rows remain in the slow selection; they are not dropped. At this checkpoint,
the remaining semantic families still needed migration before claiming the 80% target.

`mise run test` and `test:fast` run the tagged fast selection. `test:integration`
requires a filter and intersects it with non-fast scenario rows. `test:focused`
retains filters spanning either layer, and the workspace/case focused commands
retain their project-specific filters. `test:e2e` runs the non-fast scenarios and
full workspace and real-world projects explicitly. All paths use a TRX-checked
wrapper that fails if nothing executes, if the filter is invalid, if results are
missing, or if tests fail. Results and reproduction commands include the revision;
CI uses its existing console and step-summary channels. No new uploads were added.

On the same prepared local machine, the actual `mise run test:fast` command passed
156 cases in 4.22 seconds including mise, PowerShell, runner startup, incremental
build and restore checks. The wrapper reported 3.80 seconds for its dotnet invocation.
This changed-code measurement includes the newly tagged classes. The ordinary
`mise run test` alias then passed the same 156 rows in 3.87 seconds with unchanged
C# inputs (3.58 seconds inside dotnet), including the wrapper's explicit dirty-worktree
label. The initial
checkout restore measurement remains separately recorded above; neither measurement
claims an empty global package cache. A real focused integration command passed its
selected workspace callback-location case. The four-case representative workspace
selection also passed locally in 33.36 seconds inside dotnet, including Projects;
that Linux pass does not resolve the earlier macOS restore failure. Blank, missing, malformed and unmatched
filters all returned nonzero; the focused mise task also rejected an unmatched
selection that raw VSTest would otherwise report as successful.

Formatting hooks now pass `--no-restore` explicitly. Fresh checkouts and dependency
changes require an explicit restore, documented in CONTRIBUTING and AGENTS. The
mise defaults, hook configuration and both instruction documents were updated
together. No test hook was added.

A bounded sample invoked the installed `.git/hooks/pre-commit` and `commit-msg`
scripts consecutively, including their mise launch and conventional-message check.
Five samples staged this checkpoint's source edits; five staged its non-code edits
with the same source changes left unstaged. The hooks used their real stash policy.
Raw timings and output are in ignored `artifacts/devex/hooks.json` and hook logs.

| Prepared hook inputs | Samples | Total seconds per invocation pair | Nearest-rank p95 |
|---|---:|---|---:|
| Changed code | 5 | 3.530, 3.295, 3.325, 3.112, 3.311 | 3.530 s |
| Unchanged code / non-code staging | 5 | 0.101, 0.102, 0.103, 0.101, 0.101 | 0.103 s |

This is a small warm-checkout observation, not a confidence interval or a cold-hook
claim. Hooks performed no build, restore or test execution. At this checkpoint,
final fast-tier scope and cross-platform results were incomplete; this sample alone
did not establish the cost of adding tests to hooks. No test hook was added later.

CI now has independent three-OS fast and representative integration jobs plus a
formatting/workflow job. The representative job covers real Git hydration, invalid
CLI input, index/working tree, workspace parity, project references, a generator,
restored-cache/framework errors and installed packaging. It has a ten-minute timeout;
a timeout is failure, not evidence of meeting the target. Broad non-fast scenarios,
workspaces and routine real-world cases still run sequentially in a separate OS
matrix. Scheduled/manual all-case workflows and release validation remain intact.
New pushes cancel superseded runs on the same workflow/ref; cancellation remains
visible and is never counted as success. The first platform timings are recorded below.

## Historical CI evidence and failure diagnosis

The public run page and GitHub connector provide read-only CI access even when
local `gh run list` is denied. Baseline
[run 36244723276](https://github.com/collinstevens/callrift/actions/runs/36244723276),
revision `56e12bf`, completed successfully on Ubuntu and Windows. All 639 scenario
cases also passed on macOS in 56m36s, but its workspace suite failed one of 42 cases:
`WorkspaceTests.Projects`. During its first solution restore NuGet reported that
`A/obj/A.csproj.nuget.g.targets` already existed; subsequent no-restore formats
correctly reported the missing restored-cache marker. The expected snapshot is
unchanged. This predates the migrations. The physical-path reproduction and fixture
repair above subsequently established the alias collision and addressed it.

```sh
mise run workspaces:focused -- 'FullyQualifiedName~WorkspaceTests.Projects'
```

All three OS jobs in
[run 36250101113](https://github.com/collinstevens/callrift/actions/runs/36250101113)
for `ef0b199` subsequently completed successfully. Each passed 639 scenarios,
42 workspace cases and 25 routine real-world/process cases: 706 executions.
The final split preserves that total as 350 fast, 305 slow scenarios, 26 workspaces
and 25 routine cases, confirmed by the terminal logs above.

At `a23d291`, [run 36251285911](https://github.com/collinstevens/callrift/actions/runs/36251285911)
passed all 156 then-tagged fast rows independently on each supported OS. SDK was
`11.0.100-rc.1.26425.128`; restore/build preceded `-Suite Fast -NoBuild`.

| Hosted image | Image version | Complete dotnet test invocation | Separate build |
|---|---|---:|---:|
| Ubuntu 24.04 | 20260920.314.1 | 6.96 s | 9.11 s |
| Windows 2025 VS2026 | 20260922.246.2 | 7.70 s | 9.96 s |
| macOS 26 arm64 | 20260907.0351.1 | 3.90 s | 5.42 s |

These exclude PowerShell startup and tool installation and are not local-hook or
cold-restore measurements. The representative integration job passed its 12 Git/CLI
scenarios, four workspace cases and package smoke check on Ubuntu and macOS, in
roughly 2m44s and 3m27s of job logs respectively. `WorkspaceTests.Projects` passed
on macOS this time; one pass does not establish the cause of the earlier failure.
Windows integration failed before tests because concurrent SDK installers both
wrote `mise/dotnet-root/dnx.cmd`. Commit `6bffa4b` sets `MISE_JOBS=1` for Windows
setup steps in CI and scheduled Windows jobs. Workflow lint passed locally; the
Windows installation fix awaits CI. Broad verification was still running at this
inspection. Superseded jobs may be cancelled by the next checkpoint.

The subsequent [run 36251854347](https://github.com/collinstevens/callrift/actions/runs/36251854347)
for `5a36e76` passed 203 fast rows on Windows (7.15 seconds of dotnet invocation)
and Ubuntu (7.48 seconds). Both builds ran separately. Its representative integration
jobs passed on macOS and Ubuntu. Windows SDK setup and the Git/CLI selection passed;
workspace/package checks, macOS fast and broad verification were still pending at
inspection. This is evidence that the installer collision did not recur in that
run, not a claim that every Windows check finished.

At `83cfc0e`, [run 36252111787](https://github.com/collinstevens/callrift/actions/runs/36252111787)
passed all three fast jobs, formatting/workflow checks and all three representative
integration jobs. The latter completed in approximately 2m32s on Ubuntu, 4m13s on
Windows and 3m05s on macOS, measured from first/last job-log timestamps and excluding
queue time. Each ran 12 Git/CLI cases, four workspace cases and package installation
smoke checks. The three broad end-to-end jobs were still running; these fast results
do not imply a passing full suite. The Windows SDK setup fix now has passing
representative integration and packaging evidence.

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
| [CallCollectionTests](../tests/Callrift.Scenarios/CallCollectionTests.cs) | Nested expression reachability, guard order and locations | Direct source graphs and reused real workspace graphs now share the existing assertions; renderer variants reuse query/diff results. |
| [CallbackRenderingTests](../tests/Callrift.Scenarios/CallbackRenderingTests.cs) | Distinct callback bodies and per-call-site locations in renderers | Source cases now share one analysis across renderers; workspace CLI matrix retained. |
| [CancellationBoundaryTests](../tests/Callrift.Scenarios/CancellationBoundaryTests.cs) | Cancellation after analysis and during traversal | Direct cancellation checks are in the fast tier; they preserve cancellation after analysis and during traversal. |
| [ConstraintDispatchTests](../tests/Callrift.Scenarios/ConstraintDispatchTests.cs) | Constructed implementation constraints and constraint-only edits | Source rows reuse direct graphs; workspace semantic rows reuse real MSBuild graphs. Representative CLI routing and dedicated boundary suites remain. |
| [ConstructorDiagnosticTests](../tests/Callrift.Scenarios/ConstructorDiagnosticTests.cs) | Ambiguous constructor diagnostics and omitted bodies | Source rows reuse direct graphs and renderer results; workspace rows reuse actual MSBuild graphs with the same semantic and negative-control assertions. |
| [ConstructorDiscoveryTests](../tests/Callrift.Scenarios/ConstructorDiscoveryTests.cs) | Added/removed default constructors | Source rows reuse direct graphs and renderer results; workspace rows reuse actual MSBuild graphs with the same semantic and negative-control assertions. |
| [ConstructorEquivalenceTests](../tests/Callrift.Scenarios/ConstructorEquivalenceTests.cs) | Implicit versus explicit constructor equivalence, both directions and root selections | Source cases now share two analyzed graphs; workspace JSON routing and all syntax forms retained. |
| [ConstructorInitializationTests](../tests/Callrift.Scenarios/ConstructorInitializationTests.cs) | Constructor and initializer call ordering across queries | Source rows reuse direct graphs; workspace semantic rows reuse real MSBuild graphs. Representative CLI routing and dedicated boundary suites remain. |
| [DeferredCallbackTests](../tests/Callrift.Scenarios/DeferredCallbackTests.cs) | Deferred callback edges and receiver evaluation order | Source rows reuse direct graphs and renderer results; workspace rows reuse actual MSBuild graphs with the same semantic and negative-control assertions. |
| [DelegateConstructionTests](../tests/Callrift.Scenarios/DelegateConstructionTests.cs) | Delegate construction, possible callbacks and invalid targets | Source rows reuse direct graphs and renderer results; workspace rows reuse actual MSBuild graphs with the same semantic and negative-control assertions. |
| [DelegateCopyTests](../tests/Callrift.Scenarios/DelegateCopyTests.cs) | Delegate copies, factory order and incompatible return types | Source rows reuse direct graphs and renderer results; workspace rows reuse actual MSBuild graphs with the same semantic and negative-control assertions. |
| [DepthReachTests](../tests/Callrift.Scenarios/DepthReachTests.cs) | Complete versus truncated no-path results | Source rows now reuse direct graphs and results across renderers; workspace rows retain actual MSBuild analysis and the original semantic assertions. |
| [DepthReachabilityTests](../tests/Callrift.Scenarios/DepthReachabilityTests.cs) | Cycle entry reachability and bounded traversal | Cycle-entry source semantics are Fast and workspace graphs are reused. ConcurrentExpansions remains a separately selectable stress check. |
| [DepthVisibilityTests](../tests/Callrift.Scenarios/DepthVisibilityTests.cs) | Depth omissions, visible leaves and changed bodies | Source rows now reuse direct graphs and results across renderers; workspace rows retain actual MSBuild analysis and the original semantic assertions. |
| [DispatchCardinalityTests](../tests/Callrift.Scenarios/DispatchCardinalityTests.cs) | Contract identity across implementation cardinality changes | Source rows now use direct graphs; workspace rows reuse real MSBuild graphs, retaining the existing compatibility, identity and negative-control assertions. |
| [DispatchCycleIdentityTests](../tests/Callrift.Scenarios/DispatchCycleIdentityTests.cs) | Recursive dispatch and ancestor references | Source rows now use direct graphs; workspace rows reuse real MSBuild graphs, retaining the existing compatibility, identity and negative-control assertions. |
| [DispatchQueryTests](../tests/Callrift.Scenarios/DispatchQueryTests.cs) | Contract signature/root selection and possible implementations | Source rows now reuse direct graphs and results across renderers; workspace rows retain actual MSBuild analysis and the original semantic assertions. |
| [FileLocalIdentityTests](../tests/Callrift.Scenarios/FileLocalIdentityTests.cs) | File-local binding and distinct callers | Source rows reuse direct graphs across formats; workspace rows retain actual Git materialization and CLI execution. |
| [GeneratedInitializerIdentityTests](../tests/Callrift.Scenarios/GeneratedInitializerIdentityTests.cs) | Generated initializer identity, source edits and graph serialization | Sixteen direct Roslyn identity and serialization rows now live in Scenarios and are Fast. Actual generator build/input changes and fresh-process determinism remain in WorkspaceTests and InterceptorTests. |
| [GenericContextBoundaryTests](../tests/Callrift.Scenarios/GenericContextBoundaryTests.cs) | Context recursion, limits, signatures, selection and rendering | The 11 ordinary direct boundary cases are Fast. The separate budget stress class remains explicitly slow. |
| [GenericContextBudgetTests](../tests/Callrift.Scenarios/GenericContextBudgetTests.cs) | Context-budget boundaries without invented changes | Two direct context-budget stress cases remain slow; their expensive expansion is the behavior under test. |
| [GenericContextTests](../tests/Callrift.Scenarios/GenericContextTests.cs) | Invocation type arguments and downstream dispatch | Source rows reuse direct graphs; workspace semantic rows reuse real MSBuild graphs. Representative CLI routing and dedicated boundary suites remain. |
| [GenericDispatchTests](../tests/Callrift.Scenarios/GenericDispatchTests.cs) | Invariant/variant contracts and repeated type parameter unification | Source rows now use direct graphs; workspace rows reuse real MSBuild graphs, retaining the existing compatibility, identity and negative-control assertions. |
| [GitCancellationTests](../tests/Callrift.Scenarios/GitCancellationTests.cs) | Precancelled Git operations avoid inaccessible repositories | Four precancelled Git checks are Fast; actual hydration and process cancellation remain in PartialCloneTests. |
| [InterfaceReceiverTests](../tests/Callrift.Scenarios/InterfaceReceiverTests.cs) | Interface receiver constraints and abstract class implementations | Source rows now use direct graphs; workspace rows reuse real MSBuild graphs, retaining the existing compatibility, identity and negative-control assertions. |
| [PartialCloneTests](../tests/Callrift.Scenarios/PartialCloneTests.cs) | Git object hydration, ordering, errors and cancellation | Keep real repositories, fetches and child processes; retain exclusive environment collection. |
| [ProjectClassificationTests](../tests/Callrift.Scenarios/ProjectClassificationTests.cs) | Literal metadata and conditional test-project classification | Literal metadata and framework-reference classification now run directly, with shared renderer results. Evaluated multi-project test/application classification remains real CLI/MSBuild. |
| [QueryTests](../tests/Callrift.Scenarios/QueryTests.cs) | Tree/reach selection, depth/path limits, cycles, strict/exit-code behavior, merge base and invalid arguments | Retain the reviewed CLI command/format matrix, status and strict-mode behavior, argument errors and real merge-base resolution. Direct selection/traversal semantics also appear in DepthReach, DispatchQuery and context boundary classes. |
| [ReceiverConstraintTests](../tests/Callrift.Scenarios/ReceiverConstraintTests.cs) | Reference casts, conversions, sibling overrides and generic substitutions | Source rows now use direct graphs; workspace rows reuse real MSBuild graphs, retaining the existing compatibility, identity and negative-control assertions. |
| [ReceiverContextBoundaryTests](../tests/Callrift.Scenarios/ReceiverContextBoundaryTests.cs) | Receiver-state budget preserves unchanged callers | The direct receiver-budget stress case remains slow; reducing its input would change the boundary being checked. |
| [ReceiverContextDelegateTests](../tests/Callrift.Scenarios/ReceiverContextDelegateTests.cs) | Separate delegate receivers and inherited implementations | Direct source graphs and reused real workspace graphs now share the existing assertions; renderer variants reuse query/diff results. |
| [ReceiverContextFileLocalTests](../tests/Callrift.Scenarios/ReceiverContextFileLocalTests.cs) | Closed file-local contexts, revision materialization and same-basename paths | Physical-path and same-basename source semantics are Fast; both-mode revision materialization and workspace path identity remain real CLI integration. |
| [ReceiverContextTests](../tests/Callrift.Scenarios/ReceiverContextTests.cs) | Inherited receiver context, cycles and unrelated sibling edits | Source rows reuse direct graphs; workspace semantic rows reuse real MSBuild graphs. Representative CLI routing and dedicated boundary suites remain. |
| [RecordCopyPresentationTests](../tests/Callrift.Scenarios/RecordCopyPresentationTests.cs) | Clone labels, symbol identities, locations and copy order | Source rows now reuse direct graphs and renderer results; workspace rows retain real CLI/MSBuild with the same assertions. |
| [RecordCopyTests](../tests/Callrift.Scenarios/RecordCopyTests.cs) | Record copy construction, expansion and ambiguous declarations | Source rows reuse direct graphs; workspace semantic rows reuse real MSBuild graphs. Representative CLI routing and dedicated boundary suites remain. |
| [RecordCopyValidationTests](../tests/Callrift.Scenarios/RecordCopyValidationTests.cs) | Invalid record diagnostics and unavailable bodies | Source rows reuse direct graphs; workspace semantic rows reuse real MSBuild graphs. Representative CLI routing and dedicated boundary suites remain. |
| [ScenarioTests](../tests/Callrift.Scenarios/ScenarioTests.cs) | Reviewed semantic and renderer snapshots; index/working tree and invalid selection | 26 catalog rows now use shared in-memory analysis; six retain CLI format routing. Selection errors and index/working-tree snapshots stay integration. |
| [StaticCallbackInitializationTests](../tests/Callrift.Scenarios/StaticCallbackInitializationTests.cs) | Independent callback initialization state | Direct source graphs and reused real workspace graphs now share the existing assertions; renderer variants reuse query/diff results. |
| [StaticCoalesceInitializationTests](../tests/Callrift.Scenarios/StaticCoalesceInitializationTests.cs) | Conditional coalescing initialization and single receiver evaluation | Direct source graphs and reused real workspace graphs now share the existing assertions; renderer variants reuse query/diff results. |
| [StaticEventOrderTests](../tests/Callrift.Scenarios/StaticEventOrderTests.cs) | Handler evaluation before static initialization | Direct source graphs and reused real workspace graphs now share the existing assertions; renderer variants reuse query/diff results. |
| [StaticInitializationDeclarationTests](../tests/Callrift.Scenarios/StaticInitializationDeclarationTests.cs) | Unavailable initializer bodies and invalid declarations | Source diagnostics and unavailable bodies are Fast; workspace CLI checks retain diagnostic stderr routing and reuse restored inputs. |
| [StaticInitializationDepthTests](../tests/Callrift.Scenarios/StaticInitializationDepthTests.cs) | Completed initialization and later change markers | Direct source graphs and reused real workspace graphs now share the existing assertions; renderer variants reuse query/diff results. |
| [StaticInitializationIdentityTests](../tests/Callrift.Scenarios/StaticInitializationIdentityTests.cs) | Partial initializer ordering and declaring-project identity | Source partial ordering reuses direct graphs; compile-item ordering and separate projects with identical assembly/type names retain real CLI integration. |
| [StaticInitializationTests](../tests/Callrift.Scenarios/StaticInitializationTests.cs) | Conditional initialization and declaring-project links | Source rows reuse direct graphs; workspace semantic rows reuse real MSBuild graphs. Representative CLI routing and dedicated boundary suites remain. |
| [SyntaxIdentityTests](../tests/Callrift.Scenarios/SyntaxIdentityTests.cs) | Formatting-insensitive identity versus significant literal whitespace | Direct source graphs and reused real workspace graphs now share the existing assertions; renderer variants reuse query/diff results. |
| [TestAssemblyClassificationTests](../tests/Callrift.Scenarios/TestAssemblyClassificationTests.cs) | Test assembly reference recognition and conditions | Literal metadata and framework-reference classification now run directly, with shared renderer results. Evaluated multi-project test/application classification remains real CLI/MSBuild. |
| [TopLevelConstructorTests](../tests/Callrift.Scenarios/TopLevelConstructorTests.cs) | Partial top-level Program constructor binding | Source rows reuse direct graphs and renderer results; workspace rows reuse actual MSBuild graphs with the same semantic and negative-control assertions. |
| [VarianceCompatibilityTests](../tests/Callrift.Scenarios/VarianceCompatibilityTests.cs) | Variant compatibility, inheritance and nested variance | Source rows now reuse direct graphs and results across renderers; workspace rows retain actual MSBuild analysis and the original semantic assertions. |
| [VirtualDispatchTests](../tests/Callrift.Scenarios/VirtualDispatchTests.cs) | Overrides, direct base calls, exact receivers and method groups | Source rows now use direct graphs; workspace rows reuse real MSBuild graphs, retaining the existing compatibility, identity and negative-control assertions. |
| [XunitExecutableClassificationTests](../tests/Callrift.Scenarios/XunitExecutableClassificationTests.cs) | Executable test projects versus application executables | Six literal package-name classification rows now reuse direct source graphs and renderer results for each test-inclusion setting; they never required evaluated project loading. |

| Other suite | Protected behavior | Cheapest faithful layer and required boundary |
|---|---|---|
| [WorkspaceTests](../tests/Callrift.Workspaces/WorkspaceTests.cs) | Parity, packages, defines, project references, generators, framework selection and restored-cache failures | Integration for evaluation, restore, generators and references. Analyze once for renderer snapshots; preserve missing-cache and framework error cases. |
| [FrameworkDispatchTests](../tests/Callrift.Workspaces/FrameworkDispatchTests.cs) | Framework interfaces and referenced implementations | Real multi-project/framework CLI integration remains; tree and reach reuse the fixed revisions restored by diff. |
| [InterceptorTests](../tests/Callrift.Workspaces/InterceptorTests.cs) | Interceptor replacement, inserted calls and repeatable generated identities | Real generator integration and fresh-process determinism. |
| [SweepProcessTests](../tests/Callrift.RealWorldCases/SweepProcessTests.cs) | Abrupt exit, bounded output, deadlines and cancellation of child processes | Bounded process integration; these require real child lifetimes. |
| [RealWorldCaseTests](../tests/Callrift.RealWorldCases/RealWorldCaseTests.cs) | Every manifest case/view and restored Serilog | Explicit broad E2E, plus bounded routine CI views. Preserve pinned revisions, reviewed snapshots, both modes and platform coverage. |
| [Package-Smoke.ps1](../scripts/Package-Smoke.ps1) | Installed tool and dnx execution | Packaging integration; cannot be replaced with direct library calls. |

## Isolation and completion audit

Ordinary source semantics, graph/query boundaries, classification and generated
identity checks now have direct fast coverage. The retained CLI matrices exercise
argument/status/diagnostic routing and fresh-process results. Component graph reuse
is fixture-local: source pairs contain exact file contents; MSBuild pairs use unique
directories, restore each revision once, analyze through the real worker, then delete
the directories before query assertions. No mutable graph or restoration cache is
shared across scenarios. Evaluated projects, framework selection, generators,
restored-cache failures, file-local materialization and process lifetimes remain
actual integration checks. No production cache or worker lifetime was changed.

`PartialCloneTests` mutates `GIT_TRACE2_EVENT` and `GIT_NO_LAZY_FETCH`; it remains
in `ProcessEnvironmentCollection` with parallelization disabled. Other fixture
cache paths are passed through child environments, not process-wide mutation.
The complete local TRX confirms the four-class bound and exclusive collection.

No fast hook has been added. Formatting/message gates and asynchronous E2E policy
remain in force. The final fast and representative selections passed on all three
platforms, and terminal broad-suite logs confirm all 706 expected executions passed
per OS. This completed the first phase; it does not establish completion of the
reopened broad-suite performance goal in `WORKFLOW_DEVEX_GOAL.md`.


## Broad-suite performance phase: in progress

The second phase starts at `75bdd97`, with the supported-platform baseline at
`90083f5` recorded in `WORKFLOW_DEVEX_GOAL.md`. The earlier completion audit above
is historical. The ten-minute broad-feedback target and separate all-cases
cold/warm evidence remain unproven.

### Isolation inventory before changing scheduling

| Mutable state | Ownership and remaining boundary |
|---|---|
| Workspace test Git directories and caches | Each `GitFixture` creates unique physical paths and removes only its own directories after child processes finish. Cache and color settings are child-process environment entries. Independent fixtures can overlap. |
| Scenario workspace roots and restore outputs | Each `WorkspaceFixture` owns fresh before/after roots and deletes them after both worker results are read. No restored input is shared yet; repeated project shapes still restore independently. |
| Real-world repository caches | Shared by `CacheName`; preparation currently lacks a lock around clone and missing-commit fetch. Pinned revisions and remote/license checks must remain. Cold concurrent preparation must be coordinated before theory rows overlap. |
| Real-world workspace caches | Production analysis holds a file lease per digest of source/project/reference inputs, target, framework and configuration. That lease protects materialization, restore outputs and worker request/result files. It must remain; it does not protect repository preparation. |
| Process environment | Workspace and real-world tests do not mutate the parent environment. `PartialCloneTests` does and must retain its exclusive `ProcessEnvironmentCollection`. |
| Snapshot destinations | Workspace fixture names and real-world case/view names select distinct reviewed files. All renderings of one case must remain under one owner; no snapshot acceptance is part of this refactor. |
| Child processes | Workspace CLI/analysis helpers enforce deadlines and kill process trees on timeout; real-world analysis retains its ten-minute cancellation token. Sweep process tests retain actual exit, drain, deadline and cancellation checks. |

The current assembly-wide Workspaces/RealWorldCases serialization and sequential
real-world theory rows are implementation debt, not justified permanent boundaries.
Before removing them, measure concurrency one/two/four with nested MSBuild/Roslyn
work and coordinate shared repository preparation. The four-collection Scenarios
limit is provisional until the new slow-tier resource samples are available.

### Reuse the real-world diff across formats

`PinnedHistory` and `ReviewedView` now run the real Git snapshot and analysis
pipeline once per case/view, then render the same `DiffResult` independently as
text, Markdown and JSON. MSBuild views still restore and launch real workers for
both pinned revisions. View options retain entries, files, depth, external calls,
project and framework; unsupported options fail instead of being silently ignored.
The manifest, selection, test rows, reviewed snapshots and production code are
unchanged. Reuse across different views is still outstanding.

The existing snapshot envelope retains `exit: 0` and stderr formatting for byte
comparison with reviewed expectations. These envelopes now check renderer and
diagnostic content, not three separate command invocations. `RestoredSerilog`
retains actual CLI parser, format routing and exit assertions in all three formats;
`ScenarioTests.CliCallFlow`, the query/error CLI matrices and packaging retain
fresh-process command boundaries. This is the explicit coverage mapping for the
removed duplicate command pipelines; no behavior rows are deleted.

Matched local sample, 2026-09-26: Ubuntu 26.04 x64, Ryzen 9 7945HX (16 cores,
32 logical CPUs), 44,825 MiB RAM, pinned SDK `11.0.100-rc.1.26425.128`, Debug.
Before binaries were built from clean `75bdd97`; the after binaries contain the
format-reuse change in this checkpoint. Build preparation took 2.95 s for the
baseline solution and 1.79 s for the changed cases project, separately from tests.
All six invocations used `-NoBuild`, existing repository/workspace/NuGet caches,
one serial test assembly, and the existing concurrent before/after analysis.
No other benchmark, test or build ran during these samples. The filter selects
five rows: Serilog alignment guard and all four Autofac held-pipeline views,
including both source and both MSBuild views. It is a focused all-view sample,
not a measurement of either the full routine suite or `test:e2e`.

```powershell
mise exec -- pwsh -NoProfile -File scripts/Run-Tests.ps1 -Suite Cases -Filter 'DisplayName~serilog-alignment-guard|DisplayName~autofac-held-pipeline' -NoBuild
```

| Three samples per version | Before median (range) | After median (range) |
|---|---:|---:|
| Complete invocation, including mise/PowerShell/dotnet startup | 56.64 s (55.31–59.64) | 22.24 s (22.02–22.86) |
| Sampled aggregate process CPU | 252.03 s (249.96–255.65) | 106.75 s (102.94–109.10) |
| Peak summed process-tree RSS | 1.67 GiB (1.64–1.70) | 1.55 GiB (1.54–1.57) |
| Restore processes per invocation | 12 | 4 |
| Analysis worker processes per invocation | 12 | 4 |
| Summed restore process lifetime | 6.47 s (6.43–7.91) | 2.15 s (2.05–2.18) |
| Summed analysis worker lifetime | 75.20 s (73.40–75.38) | 24.78 s (24.64–25.65) |

All five rows passed in every run, with no reviewed-output changes. Median elapsed
time fell 60.7% and sampled CPU fell 57.6%, with unchanged scheduling and runner
count. On this single local worker, aggregate command time equals elapsed time:
0.944 to 0.371 worker-minutes for the selection. Hosted runner-minutes and broad
critical-path improvement are not measured by this sample.

The ignored `artifacts/devex/phase2/cases-{before,after}*.json` and corresponding
logs contain the measurements. A local `/proc` observer sampled descendants every
50 ms; CPU and lifetimes are estimates that can miss very short-lived or reparented
children, and summed RSS includes shared pages. Worker lifetime combines startup,
project loading, compilation, graph analysis and serialization; it does not yet
separate those stages. Restore timing is warm package/cache work. Cold repository
preparation, deeper stage attribution, cross-view reuse, bounded concurrency,
workspace/scenario setup reduction, supported-platform targets and matched full
all-cases E2E evidence remain outstanding.

A separate focused validation passed three existing rows in 22.26 s complete
invocation time: CleanArchitecture logging (`--file`), Autofac any-key source
focused (`--externals`) and `RestoredSerilog` (actual CLI text/Markdown/JSON).
No tests or snapshots were added. The fast tier and test command definitions are
unchanged; this checkpoint does not claim a new full-suite or fast-tier measurement.

### Workspace format reuse and bounded scheduling

Eight of the nine workspace snapshot rows now analyze the revision pair once,
then render text, Markdown and JSON against their unchanged reviewed snapshots.
The direct fixture now accepts the workspace target, preserving actual solution
loading, project identity/references, package binding, defines and SDK generators.
`Parity("guard")` still executes all three CLI formats. Referenced-generator,
framework-selection, missing-cache, interceptor and fresh-process assertions remain
real CLI integrations. The literal success envelopes and revision placeholders in
the eight renderer rows now describe renderer expectations; CLI status/identity
routing remains covered by the retained command tests.

Before changing scheduling, the same four-row filter used in the prior local
profile ran three times before and three times after format reuse. Before: binaries
from `077f2ab`. After: this checkpoint's renderer changes with assembly serialization
still enabled. The prepared Debug build took 2.52 s separately. Machine, SDK and
50 ms observer are as above; all 32 logical CPUs were available. Fixture repositories
and workspaces were fresh per row; global SDK/NuGet packages were warm. No concurrent
local build or suite ran. Selected rows: guard parity, Projects, PackageBinding,
and Generator. The guard row intentionally retains its original three CLI calls.

```powershell
mise exec -- pwsh -NoProfile -File scripts/Run-Tests.ps1 -Suite Workspaces -Filter 'FullyQualifiedName~WorkspaceTests.Projects|FullyQualifiedName~WorkspaceTests.Generator|FullyQualifiedName~WorkspaceTests.PackageBinding|(FullyQualifiedName~WorkspaceTests.Parity&DisplayName~guard)' -NoBuild
```

| Three samples per version | Before median (range) | After median (range) |
|---|---:|---:|
| Complete invocation | 39.66 s (38.71–39.94) | 30.03 s (30.01–30.75) |
| Sampled aggregate process CPU | 87.08 s (85.05–87.14) | 43.91 s (43.75–44.70) |
| Peak summed process-tree RSS | 1.43 GiB (1.42–1.43) | 1.17 GiB (1.16–1.17) |
| Analysis worker processes | 24 | 12 |

Every row passed, with no accepted snapshot changes. This reduces work before
adding concurrency: sampled CPU fell 49.6%, while invocation time fell 24.3%.
The reused direct fixture currently processes the two revisions sequentially;
that limits elapsed improvement relative to the original CLI, which overlaps
its before/after workers. Raw observations are in ignored
`artifacts/devex/phase2/workspace-{before,after}*.json` and corresponding logs.

For the first scheduling experiment, package, define, solution, SDK generator, referenced
generator, framework selection, empty framework and cache-error methods now live
in separate classes in `WorkspaceTests.cs`. Their method names and InlineData are
unchanged, and every new class name ends in `WorkspaceTests`, retaining the
existing `FullyQualifiedName~WorkspaceTests.Method` filters. Parity still has five
rows; framework selection has four. The separate FrameworkDispatch and Interceptor
classes were unchanged in that experiment. There remain exactly 26 existing workspace rows, with no
new test or snapshot files. Each independent collection owns unique fixture paths;
no parent process environment or mutable graph is shared.

Concurrency experiments use the [xUnit VSTest settings](https://xunit.net/docs/config-runsettings)
`xUnit.MaxParallelThreads=1`, `2`, and `4`, with the conservative scheduler. The
entire test process tree is restricted to CPU affinity `0-3` on this local host,
so nested MSBuild/Roslyn work shares four CPU cores. This is a controlled local
resource experiment, not a substitute for hosted Ubuntu/Windows/macOS evidence.


The first full-suite scheduling experiment passed all 26 rows at each limit:

| Concurrent collections | Invocation | Sampled CPU | Peak summed RSS | Observed overlapping tests | Sum of case durations |
|---|---:|---:|---:|---:|---:|
| 1 | 237.01 s | 333.79 s | 1.47 GiB | 1 | 236.02 s |
| 2 | 132.82 s | 346.81 s | 1.75 GiB | 2 | 252.43 s |
| 4 | 163.58 s | 360.08 s | 2.62 GiB | 4 | 327.75 s |

These are single samples at each limit, not medians. All used 103 analysis
workers. Concurrency two reduced latency; four increased contention and the
serial tail. Neither reduced work by itself. At concurrency two, the six-row
interceptor collection occupied 122.72 s of the 132.82 s invocation. TRX timestamps
prove actual case overlap; process samples include nested workers. The raw
`workspaces-concurrency-{1,2,4}.{json,trx,log}` files are ignored local artifacts.

The next change divides the existing interceptor rows into four collections:
replacement-body determinism (one row), inserted-call preservation (one), implicit
static initialization (the original two `explicitConstructor=false` rows), and
explicit static initialization (the original two `explicitConstructor=true` rows).
Method names and both Boolean data arguments remain, including unchanged and
changed generator inputs. The four initialization rows still compare two actual
CLI diff processes for identical output. Their tree/reach format-only assertions
now reuse one separately restored, generated and serialized after graph; all
existing content/identity assertions remain. The replacement-body row still runs
the full CLI tree/reach format matrix, including success status assertions. This
removes 20 redundant analysis workers per full suite without weakening the
fresh-process determinism boundary. The single-snapshot fixture owns and cleans
its own root, request and result files under the same timeout/child cleanup policy.

### Coordinate shared real-world repository preparation

`RealWorldCaseStore.PrepareAsync` now holds a file lease per cache name across
clone, remote validation, missing-commit fetch and pinned license validation.
Different repositories can prepare independently. Lease waits respect caller
cancellation and have a bounded retry count. Lock files remain on disk to avoid
unlink/recreate races between waiting processes. A cold clone is created in a
unique sibling staging directory and moved into place only after success; failed
staging directories are cleaned without deleting any published cache directory.
This is preparation coordination, not a lock around analysis or renderer assertions.
The real-world assembly remains serial until case scheduling and resource limits
are validated.

A temporary probe, removed before committing, ran four separate preparation
processes against one initially absent Serilog cache using the manifest's GitHub
remote and pinned commits. Cold preparation completed in 1.18 s and warm preparation
in 0.41 s on the machine above. These are one-off preparation observations, not
full case timings or an empty SDK/NuGet-cache claim. A four-caller missing-commit
fetch probe also passed. Cancellation behind a held lease, a subsequent successful
acquisition, remote mismatch and license mismatch were checked; no clone staging
directories remained. Logs are in ignored `preparation-{cold,warm}.log`. This does
not yet prove cross-platform or whole-suite concurrent cold preparation.

With the interceptor graph reuse and collection split, the same complete suite
passed at both candidate bounds (one sample each, same four-core affinity and
warm global packages/fresh fixture roots):

| Concurrent collections | Invocation | Sampled CPU | Peak summed RSS | Observed overlapping tests | Analysis workers |
|---|---:|---:|---:|---:|---:|
| 2 | 113.50 s | 291.82 s | 1.87 GiB | 2 | 83 |
| 4 | 93.67 s | 315.60 s | 3.22 GiB | 4 | 83 |

Four is the chosen workspace bound: it improves latency after reducing the
long collection, with a measured memory tradeoff. Compared with the earlier
four-collection experiment, worker launches fell 19.4%, sampled CPU fell 12.4%
and invocation time fell 42.7%. Increasing the bound alone was counterproductive
before the interceptor refactor. These measurements do not establish hosted
memory peaks or platform targets.

`WorkspaceCollectionOrderer` schedules the measured longest collections first:
framework dispatch, parity, replacement-body determinism, explicit/implicit
initializers and mixed-framework selection. It does not filter tests or alter
assertions. Its purpose is to fill free slots with shorter independent cases as
long collections finish. The remaining theory rows within a family are still
sequential; hosted measurements must determine whether these tails need further
splitting. The environment-mutating scenario collection remains exclusive and
unchanged.

The final checked-in workspace bound and ordering passed two consecutive full
26-row runs with no runner overrides. Four-core affinity was retained; every run
used fresh fixture roots and warm global packages. Complete invocation median was
87.89 s (87.26–88.52), sampled CPU 319.71 s (318.98–320.43), and peak summed RSS
2.86 GiB (2.78–2.95). Both TRX files show at most four overlapping tests; process samples record 83
analysis worker launches in each run. Local command occupancy is 1.46 worker-minutes per
invocation, on one worker. These are the final local stability samples, recorded
as `workspaces-final-{1,2}`. No hosted runner or extra CI job was added by this
checkpoint; hosted time and memory remain unverified.

### Attribute remaining scenario worker costs

Two existing record-copy integration rows (`inherited-copy` and
`direct-cross-project`) were profiled on four-core affinity with fresh roots and
warm global SDK/packages. The prepared Debug command passed in 13.71 s, using four
restores (observed lifetimes 0.42–0.51 s) and four fresh analysis workers. Temporary
stopwatch instrumentation separated reference preparation, workspace creation,
project opening, framework selection, compilation, member/diagnostic collection,
dispatch construction and serialization. It was removed afterward; production
worker sources are unchanged.

A second temporary probe repeated the complete `WorkspaceAnalysis.AnalyzeAsync`
call three times per worker on the identical request, creating/discarding its
workspace each time. Both existing rows still passed. The 24.13 s invocation does
three times the analysis work and is not an improved test-command measurement.
The following medians compare the first call in each of four workers with the
subsequent eight calls in those same workers:

| Stage | First call | Repeated call |
|---|---:|---:|
| Reference preparation/evaluation | 0.693 s | 0.607 s |
| Create workspace/MEF host | 0.125 s | 0.032 s |
| Open project/solution | 0.554 s | 0.436 s |
| Select frameworks/evaluate projects | 0.151 s | 0.153 s |
| Get compilations | 0.418 s | 0.070 s |
| Collect members and diagnostics | 0.332 s | 0.017 s |
| Build dispatch map | 0.001 s | <0.001 s |

Whole analysis ranged 2.07–3.05 s on first calls and 1.11–1.55 s on repeated
calls; medians of individual stages need not sum to the median total. Request
parsing, MSBuild registration and graph serialization each took roughly 15–25 ms
in the first probe. These instrumented observations include measurement overhead
and a mixed single-/multi-project sample. They show that warm compilation/member
collection can help, but worker persistence alone still repeats substantial
project evaluation and loading. Restoration and safe reuse of evaluated inputs
remain targets; no persistent worker or production analysis cache was introduced.
Raw stage files are ignored under `worker-stages` and `worker-warm-stages`.

After restoring and rebuilding the uninstrumented sources, `mise run test:fast`
passed all 350 rows in 4.31 s including mise/PowerShell/dotnet startup and the
up-to-date build. The prepared development budget remains below ten seconds.
No fast/focused/E2E command or commit/push gate was changed.


The existing Serilog alignment snapshot also passed from an initially absent
relative `CALLRIFT_CASES_CACHE` directory in 4.67 s. Staging destinations are
absolute so relative cache configuration still clones into the intended directory.
Reviewed expectations and all pinned manifest content remain unchanged. The
checkpoint's full supported-platform CI and all remaining goal criteria are still
pending; these local samples do not close the broad-suite performance goal.

### Reuse pinned graphs across views and schedule repositories independently

The existing manifest rows now live in seven repository-specific test classes.
Each class owns one `RealWorldCaseFixture`; rows within that repository remain
serial while independent repositories can overlap. Discovery was compared as an
exact multiset of `(id, viewId)` against the manifest: all 90 manifest rows plus
`RestoredSerilog` and four process rows remain (95 full, 25 routine). Unknown
repository names or a change between history and view rows fail discovery until
the corresponding class is updated. Existing method-name filters still match.

The fixture keeps at most one analyzed before/after pair. Its key includes the
absolute repository path, both pinned revisions, provider mode, full MSBuild
options and test inclusion. Rows are ordered so views with identical analysis
inputs are adjacent. Each view still runs preparation and its remote, revision
and license checks; each independently compares and renders its own options and
checks the existing reviewed files. Switching inputs releases the previous pair,
and class disposal clears the final pair. No production graph cache, manifest,
reviewed expectation or CLI/process assertion changed.

The same five-row Serilog/Autofac sample above passed in 12.62 s with 50.55 sampled
CPU seconds and 1.63 GiB peak summed RSS after this change. This is one preliminary
sample with two available collection slots, the same warm caches and native CPU
affinity; it is not a matched median for scheduling. The directly observed work
reduction is four MSBuild workers/restores to two: the two Autofac MSBuild views
share one pinned pair, as do the two source-only views. All reviewed snapshots
remained unchanged. Bounded one/two/four-collection routine measurements follow.

The full routine selection then ran once at each limit, sequentially on CPU
affinity `0-3`, using the same machine, SDK and Debug binaries, existing repository
and workspace caches, and `dotnet test --no-build`. Each invocation passed all
25 rows. TRX start/end times confirmed actual overlap; child process profiles
confirmed 14 analysis workers at every limit. The command supplied
`-- xUnit.MaxParallelThreads=N xUnit.ParallelAlgorithm=conservative`.

| Collection limit | Complete invocation | Sampled CPU | Peak summed RSS | Actual overlap |
|---|---:|---:|---:|---:|
| 1 | 169.52 s | 333.22 s | 6.53 GiB | 1 |
| 2 | 101.93 s | 276.50 s | 4.81 GiB | 2 |
| 4 | 108.13 s | 354.36 s | 6.88 GiB | 4 |

Two was the provisional assembly limit after this first sweep. These single
observations are not an isolated comparison: xUnit's default collection ordering
varied between runs, as the TRX timelines confirmed. At two, ASP.NET Core started last
and left about 32 seconds without another active collection. An experimental
order that started it first alongside Autofac took 117.13 s median over two warm
runs (113.76–120.50), with 349.07 sampled CPU seconds (341.86–356.27) and
5.66 GiB peak summed RSS (5.65–5.67). Its cold
run passed in 131.60 s, with 385.53 CPU seconds and 6.33 GiB RSS; seven clone
processes totaled 8.63 seconds of observed lifetime. The override was removed:
shortening the final serial stretch did not improve complete invocation time.
The final orderer makes the faster observed order explicit: Serilog, Autofac,
CleanArchitecture, process checks, Orchard Core, Polly, restored Serilog, Ocelot,
then ASP.NET Core. A second one/two/four sweep uses that identical ordering at
every limit, with the same prepared external caches. Raw initial profiles and
TRX files use `cases-concurrency-{1,2,4}`; the rejected early-large-repository
experiment used `cases-final-*` names; the controlled comparison uses
`cases-fixed-order-{1,2,4}`.

| Fixed collection order | Complete invocation | Sampled CPU | Peak summed RSS | Actual overlap |
|---|---:|---:|---:|---:|
| 1 | 130.72 s | 276.12 s | 4.55 GiB | 1 |
| 2 | 102.58 s | 278.64 s | 4.73 GiB | 2 |
| 4 | 106.77 s | 330.17 s | 4.46 GiB | 4 |

All three controlled runs passed the same 25 rows and started 14 analysis workers.
Two is the final assembly limit: it reduced invocation time by 21.5% against one
with almost unchanged CPU, while four used 18.5% more CPU than two and took longer.
Four's modestly lower sampled memory peak did not improve throughput. The final
two-slot result also agrees with the earlier 101.93 s run that happened to use
this order, but those two observations used different prepared cache directories.
On one local worker, the controlled command totals were 2.179, 1.710 and 1.779
worker-minutes respectively; these are scheduling comparisons after format reuse,
not a claim that more slots reduced the number of analyses. Build preparation
took 1.81 s separately. No other local build, test or benchmark overlapped.

### Supported-platform checkpoint before splitting suites

[CI run 36277607859](https://github.com/collinstevens/callrift/actions/runs/36277607859)
tests `5970818`, containing format reuse, workspace changes and preparation
coordination, before repository scheduling and cross-view graph reuse. All three
platforms passed the 305 scenario rows and 26 workspace rows. Ubuntu and macOS
also passed all 25 routine case/process rows; Windows case verification was still
pending when these results were recorded. Fast, representative integration and
quality checks passed on every applicable platform.

| Complete CI test step | Ubuntu 24.04 | Windows | macOS 26 ARM64 |
|---|---:|---:|---:|
| Slow scenarios | 26m 21s | 29m 51s | 22m 19s |
| Workspaces | 4m 17s | 5m 15s | 2m 31s |
| Routine cases/processes | 5m 15s | Pending | 5m 32s |
| Complete broad job | 36m 32s | Pending | 31m 09s |

The completed Ubuntu and macOS job logs explicitly report repository-cache
misses, so these case timings include cold repository preparation. The workspace
stage improved on all three platforms but misses the two-minute target everywhere.
Scenarios remain unchanged in scope and far above six minutes, with substantial
runner variation. Local four-CPU timings do not establish hosted-runner targets.
No supported-platform completion criterion is checked on this evidence.

### Independent broad CI suites

Broad verification now has one job per suite and OS: the same 305 slow scenarios,
26 workspace rows and 25 routine real-world/process rows on each of Ubuntu,
Windows and macOS. The commands and selections are unchanged. Each job restores
and builds its own test project and dependency closure; only case jobs download
the repository cache. Existing failure artifacts include the suite in their names
to prevent collisions. Fast, representative integration and quality jobs remain
independent.

This changes three broad jobs to nine and repeats setup/build work. It makes
workspace and case results available without waiting for slow scenarios, but
does not itself reduce test work or prove any target. Measure the complete broad
dependency path as the latest required suite completion per OS, report queue
delay separately, and sum all nine job durations for broad runner-minutes. The
first supported-platform results and measured extra setup cost remain pending.

The first split-suite run exposed why repository caches never became warm.
[Run 36279765268](https://github.com/collinstevens/callrift/actions/runs/36279765268)
passed cases on Ubuntu and macOS, but their post-job cache save reported that
relative `.` and `..` path components are not allowed. The old absolute-looking
`${{ github.workspace }}/../callrift-real-world-cases` retained a literal `..`.
This was also present in the preceding completed macOS job. A successful test job
therefore did not prove that repository preparation would be cached next time.

Both the test environment and cache action now use the same path below
`${{ runner.temp }}`, outside the checkout and without parent components. The
manifest and OS cache key remain unchanged. A successful archive save and a
subsequent reported cache hit are required before labeling CI results warm;
neither has been established at this checkpoint. The first split-suite case
steps took 3m 57s on Ubuntu and 5m 58s on macOS with cache misses, so they do not
meet the warm three-minute target or demonstrate completion of the goal.

[Run 36280349298](https://github.com/collinstevens/callrift/actions/runs/36280349298)
then confirmed successful archive saves on Ubuntu, Windows and macOS at
`ab612fd`. Each case job passed all 25 rows. The dotnet command portions took
251.04 s, 399.75 s and 370.18 s respectively; complete jobs including setup and
cache upload took 4m 51s, 8m 14s and 7m 24s. All three were cache misses. Archive
creation is now proven, but a restored-cache run is still required for warm CI
timing. The connected GitHub app rejected an individual job rerun with HTTP 403
because it lacks Actions write permission; no warm rerun was started by that
request. A subsequent checkpoint's normal workflow can validate restoration.

That run subsequently passed every required job. Its broad test-step and complete
job timings were:

| Scope | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Slow scenarios, 305 rows | 26m 06s | 23m 14s | 14m 20s |
| Workspaces, 26 rows | 3m 12s | 3m 38s | 3m 53s |
| Cold routine cases, 25 rows | 4m 14s | 6m 41s | 6m 13s |
| Broad dependency path from first job start | 26m 37s | 24m 36s | 14m 54s |
| Sum of the three broad jobs | 35m 14s | 38m 00s | 26m 48s |

These are workflow step timings, including the PowerShell test wrapper, rather
than the narrower dotnet command timings above. Broad jobs started 82–89 seconds
after workflow creation. Including that initial scheduling delay, the final broad
result arrived after 27m 59s, 25m 58s and 16m 20s respectively. The nine broad jobs
consumed 100.03 runner-minutes versus 143.82 in the original three-job baseline.
Their combined non-test overhead was 8m 31s, including repeated setup/build and
cache save, compared with approximately 3m 19s originally. Fast, integration and
quality jobs are excluded from both broad runner-minute totals.

The scenario harness was still unchanged in this run; its large macOS variation
cannot be attributed to the uncommitted worker experiment. The observed total
reduction combines earlier reductions in workspace/case work with runner
variation. Every suite still exceeds its target, and the warm case condition has
not yet been measured on hosted runners.

The documentation-only checkpoint `30d831f` then ran the same committed code with restored
repository caches in [run 36282389307](https://github.com/collinstevens/callrift/actions/runs/36282389307).
All three case logs explicitly reported primary-key `cases-*` cache hits and
successful restoration. Every OS passed all 25 rows:

| Prepared repository-cache cases | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Complete dotnet test command | 239.71 s | 343.12 s | 209.04 s |
| Complete case job, including setup | 4m 40s | 7m 10s | 4m 09s |

This verifies the cache-path correction but misses the three-minute case target
on every platform. Only repository caches are restored by CI; restored workspace
outputs remain fresh per job. A cache hit therefore does not remove project
restoration, worker startup or graph analysis. The remaining broad jobs were
still running when these case results were recorded.

### Full local E2E baseline

The first attempted full baseline was stopped during real-world cases because
the profiling setup placed the external workspace cache below this checkout.
Those projects inherited Callrift's `Directory.Packages.props` and build settings,
causing an Ocelot restore failure. That invalid run is retained as ignored
`e2e-invalid-nested-cache` artifacts and is excluded from performance comparisons.
Matched full-command measurements must use external cache directories outside
the checkout; repository and workspace caches start empty for the cold sample,
while the SDK and global NuGet cache are prepared.

The corrected cold run at `75bdd97` (the baseline source plus documentation)
passed all 426 executions: 305 scenarios, 26 workspaces and all 95 pinned cases.
On the Ubuntu 26.04 Ryzen 9 7945HX development host, with native 32-logical-CPU
affinity, 44,825 MiB RAM, SDK `11.0.100-rc.1.26425.128` and Debug binaries,
`mise run test:e2e` took 3269.90 s (54m 30s). Its dotnet command portions were
453.63 s, 260.20 s and 2555.67 s respectively. Explicit preparation before the
measurement took 1.21 s to restore and 2.84 s to build. Repository and workspace
caches were absent initially and lived outside the checkout; the SDK and global
NuGet cache were already prepared.

The process-tree sampler recorded 15,532.82 CPU-seconds and a peak summed RSS of
16.60 GiB. These are sampled descendant-process measurements, not machine-wide
CPU or unique resident memory. The warm pass will retain the same checkout and
cache directories. Focused worker validation is scheduled between cold and warm
passes, with no overlapping measured runs; the warm result remains pending.

### Reusable scenario workers and restored project shapes

At checkpoint `06a33c1`, the scenario harness gained a lazy pool of at most four dedicated workers.
Each request still calls production `WorkspaceAnalysis.AnalyzeAsync`, creates a
fresh MSBuild workspace, compiles and analyzes the supplied source, and serializes
the complete graph. It does not cache graphs or loaded Roslyn solutions. The
existing scenario collection limit remains four; worker processes are leased
exclusively across both revisions of one case and disposed at assembly teardown.
Requests have a three-minute deadline, failed workers are discarded, and parent
exit terminates the worker tree. Owned roots are deleted after worker exit.

A restored root is reused only for one plain SDK project with identical complete
project XML, source paths and MSBuild options. Every source is rewritten for every
revision. The guard rejects custom imports/targets, conditional or unknown
properties, expressions, packages, analyzers, extra inputs, path aliases and
ambient directory build files. Recognized multi-project fixtures use a warmed
worker but fresh roots and restores for every revision. Unknown shapes retain the
original fresh-worker path. Dedicated CLI and fresh-process determinism checks
remain intact, including record-copy format routing and file-local identities.

Sequential matched samples used the prepared Debug build and pinned SDK on the
same development host, with no other measured run overlapping:

| Selection | CPU affinity | Baseline wall / CPU | Worker wall / CPU | Peak summed RSS before / after |
|---|---|---:|---:|---:|
| Inherited and direct cross-project record copy, 2 rows | Native 32 CPUs | 12.87 / 14.64 s | 10.01 / 14.83 s | 0.71 / 0.94 GiB |
| Constraint dispatch, static initialization, constructor initialization and record copy, 95 rows | CPUs 0–3 | 218.61 / 607.65 s | 98.76 / 319.63 s | 1.90 / 2.95 GiB |

Both 95-row samples reached four overlapping cases and had identical case-name
multisets, with every row passing. Restores fell from 190 to 20; fresh analysis
workers fell from 198 to 10, alongside four pooled workers. This reduces sampled
work as well as elapsed time, but retains more memory. The smaller two-row sample
does not show a CPU improvement. These single matched samples do not establish
full-suite targets, timing variance, the best concurrency limit or platform
stability; supported-platform CI remains required.

The complete fast command passed all 350 rows in 4.63 s including mise, PowerShell,
dotnet startup and the up-to-date build, and launched no fixture workers. Four
existing project-classification/file-local boundary rows and two workspace
define/referenced-generator rows also passed. Temporary probes checked shape-key
invalidation and fallback, malformed-request recovery, newline-containing request
paths, changed source, EOF and parent-exit termination with stdin held open. Probe
sources were removed; no permanent tests or reviewed snapshots were added or
changed. The test wrapper now prints changed paths when its revision is marked
dirty, making future CI source-state reports more specific.

### Supported-platform worker checkpoint

[Run 36283119561](https://github.com/collinstevens/callrift/actions/runs/36283119561)
at `06a33c1` passed all required fast, representative integration, quality and
broad jobs. Each OS retained 305 slow scenarios, 26 workspace rows and 25 routine
cases. All three case jobs explicitly restored their primary-key repository
caches; workspace outputs were fresh per job.

| Complete dotnet test command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Slow scenarios | 925.30 s | 690.98 s | 597.46 s |
| Workspaces | 154.99 s | 304.76 s | 250.38 s |
| Routine cases, prepared repository caches | 264.33 s | 356.12 s | 207.53 s |
| Broad path, earliest suite job start to final required suite result | 16m 17s | 13m 43s | 10m 38s |

The nine broad jobs consumed 72.22 runner-minutes, including their setup and
teardown. This compares with 143.82 minutes for the original three sequential
broad jobs, but the original caches missed, so it is not a matched warm-cache
comparison. Scheduling before the earliest broad job is excluded from these
path timings and must be reported separately for the final target assessment.
All suite targets and the ten-minute broad target remain unmet. The scenario
improvement is consistent with the focused reduction in restores and worker
starts; hosted timings alone do not isolate its effect from runner variance.

Dirty-checkout reports inspected in this run identify `mise.lock` as the changed
file. No reviewed test expectations were changed by this checkpoint.

### Completed original warm E2E measurement

The original warm full command at `75bdd97` passed the same 426 executions in
3238.36 s (53m 58s), retaining the cold run's external repository and workspace
caches, prepared SDK and global NuGet cache. Its scenario, workspace and all-case
dotnet portions were 480.08 s, 260.30 s and 2497.64 s. The cold-to-warm gap was
737.53 s, during which focused worker validation ran; no measured runs overlapped.
The sampler recorded 15,511.52 descendant CPU-seconds and 16.47 GiB peak summed
RSS. It observed 1014 analysis workers and 805 restore commands in each original
run. Warm caches reduced total elapsed time by only 0.96% in this single pair;
they did not remove repeated analysis and process startup. Matched optimized
cold/warm full-command measurements are still outstanding.

### Rejected scheduling and materialization experiments

On CPUs 0–3, the two existing AspNetCore and Orchard source-roots rows were
measured twice per variant with prepared repository caches. The first pair ran
parallel then sequential before/after analysis; the repeat reversed that order.
All selected rows passed and the fixture source and binaries were restored after
each experiment.

| Pair scheduling | Wall seconds, first / repeat | Sampled CPU seconds, first / repeat | Peak summed RSS GiB, first / repeat |
|---|---:|---:|---:|
| Parallel | 79.83 / 73.13 | 235.92 / 193.25 | 5.06 / 6.16 |
| Sequential | 72.08 / 76.91 | 164.99 / 191.86 | 5.01 / 5.98 |

The first CPU improvement did not repeat, and elapsed-time ordering reversed.
The fixture therefore keeps its existing pair scheduling. Separately, avoiding
writes of identical cached snapshot bytes passed five existing interceptor and
framework-dispatch CLI rows but changed elapsed time only from 39.92 to 39.29 s
and sampled CPU from 85.90 to 84.95 s. That production change was not adopted.
These samples do not establish a useful improvement beyond normal variation.

### Retaining eligible scenario workspaces

The fixture worker now retains a loaded MSBuild workspace for consecutive requests
with the same strict single-project shape, root and options. It rereads all loaded
regular source documents, updates changed Roslyn source text and runs the graph
analysis and compilation diagnostics for every request. SDK source generators
observe the updated solution. Shape, root or option changes discard the loaded
workspace. Multi-project fixtures still load fresh, and unsupported shapes retain
the original fixture path. The shape guard additionally rejects bin/obj input
paths, path-bearing assembly names and self references.

Production loading and analysis share the same implementation through an internal
loaded-workspace owner. The public production entry point continues to load,
analyze and dispose a fresh workspace per call; only the fixture tool has friend
access to retain it. Errors discard retained state. Parent exit, EOF, bounded
requests and assembly teardown retain their existing cleanup ownership.

Twelve temporary probes compared complete raw graphs from the original production
worker, the refactored fresh worker and the retained worker. They matched across
source edits, introduced and cleared errors, SDK regex generator changes, added
and renamed files, project-property changes, Debug/Release options and different
roots. Separate probes checked shape rejection and malformed-input recovery, EOF
and parent-exit termination after a workspace had loaded. These probes do not
replace the existing integration expectations or supported-platform CI.

The same 95-row sample on CPUs 0–3 passed in 38.89 s with 120.42 sampled CPU-seconds
and 2.55 GiB peak summed RSS, compared with 98.76 s / 319.63 CPU-seconds / 2.95 GiB
for the worker-only checkpoint. Exact case-name multisets matched the original
baseline and worker-only samples. All three reached four overlapping rows. The
new sample still used 20 restores, ten fresh workers and four pooled workers;
its additional reduction comes from avoiding repeated project loading inside
eligible pooled workers, not fewer executed rows or CLI checks.

The concurrency comparison retained the exact same 95 rows and four-CPU affinity:

| Collection limit / observed overlap | Complete command | Sampled CPU | Peak summed RSS | Pooled workers |
|---|---:|---:|---:|---:|
| 1 / 1 | 60.09 s | 88.38 s | 1.45 GiB | 1 |
| 2 / 2 | 43.44 s | 101.93 s | 2.00 GiB | 2 |
| 4 / 4 | 38.86 s | 120.98 s | 2.53 GiB | 4 |

Four remains the limit for lower latency, with its higher CPU and memory cost
explicitly retained. The two four-collection samples have a 38.88 s median and
38.86–38.89 s range; these are short local samples, not a hosted-runner guarantee.
The complete fast command still passed 350 rows in 4.64 s with no fixture workers.

The existing four project-classification/file-local integration rows passed in
32.38 s, and eight mixed-framework, referenced-generator, define and cache rows
passed in 18.85 s. The full solution built without warnings or errors. Temporary
probe sources were removed before the checkpoint; reviewed expectations remain
unchanged. Supported-platform CI for this loading refactor is pending, and no
hosted-suite or full E2E target is claimed from these focused results.

### Loaded-workspace CI and remaining hot paths

[Run 36286698303](https://github.com/collinstevens/callrift/actions/runs/36286698303)
at `4ed0fd0` passed every required job on all three platforms. Each retained all
305 scenarios, 26 workspaces and 25 routine cases. Every case job restored its
primary-key repository cache.

| Complete dotnet test command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Slow scenarios | 504.83 s | 763.23 s | 551.59 s |
| Workspaces | 183.79 s | 208.20 s | 319.39 s |
| Routine cases, prepared repository caches | 265.21 s | 345.16 s | 355.98 s |
| Broad path, earliest suite job start to final suite result | 8m 58s | 14m 15s | 9m 56s |

The nine broad jobs consumed 67.80 runner-minutes including setup and teardown.
Ubuntu and macOS met the broad latency target in this warm-cache run; Windows
and every per-suite target remain above budget. Windows scenarios were slower
than the worker-only checkpoint despite the local focused improvement, so the
local sample does not establish a supported-platform speedup by itself. The
wrapper now reports the five slowest cases and classes by accumulated case time
from the existing TRX result, without uploading additional artifacts. Class sums
accumulate individual row elapsed times; they are not CPU or whole-job time.
Workflow creation preceded the first broad job by 2 s on Ubuntu and Windows,
and 5 s on macOS; that initial scheduling delay is separate from the paths above.

A temporary stage-timing sample of the existing AspNetCore and Orchard source
rows measured roughly 20–24 s in member collection per revision, another 5–6 s
in instance-initializer processing, and about 1–2 s in dispatch. Concurrent stage
wall times overlap and must not be added as CPU time. Instrumentation was removed.

Two further experiments were rejected. Buffered Git reads reduced an isolated
11,229-blob read from 0.41–0.53 s to 0.22–0.32 s with identical complete-content
hashes, and preserved six binary sizes including empty files and the 64 KiB
boundary. However, the complete source-pair command took 82.44 s / 246.53 sampled
CPU-seconds, versus the earlier 73.13 s / 193.25 s control. Sequential revision
analysis with buffering still took 77.89 s / 196.63 s. Earlier availability of
inputs changed overlap; the isolated I/O gain did not establish a suite gain.
Per-analysis memoization of method keys and labels likewise took 77.58 s / 227.37
CPU-seconds. All selected snapshots passed, but neither change was retained.

### Timing-report checkpoint

[Run 36287556821](https://github.com/collinstevens/callrift/actions/runs/36287556821)
at `1a7c623` passed all required checks with the same production and harness code
as `4ed0fd0`, plus timing output. It retained 305 / 26 / 25 broad executions per OS.
All case jobs restored their repository caches.

| Complete dotnet test command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios | 416.33 s | 771.68 s | 751.45 s |
| Workspaces | 276.52 s | 317.07 s | 255.69 s |
| Cases | 257.58 s | 357.46 s | 262.71 s |
| Broad path | 7m 40s | 14m 24s | 13m 37s |

Broad runner time was 70.73 minutes. Initial scheduling delays were 2 / 3 / 7 s
respectively. This repeat demonstrates substantial hosted variance: macOS no
longer met the broad target, while Ubuntu improved. No per-suite target was met.
The largest scenario class totals were constructor equivalence and static
initializer declarations, both of which still repeat full CLI analysis across
semantic/query permutations. Framework-dispatch and parity classes led workspace
class totals; the generated-interceptor CLI row alone took 137–156 s. These are
specific remaining targets, not grounds to drop those behaviors or assertions.

A bounded managed-thread trace resolved method names after an initial trace lost
its method-resolution data at process exit. Its sampling includes waiting time,
so thread-stack percentages are not CPU utilization. GC polling motivated a
case-host server-GC experiment using the documented
[per-process GC setting](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#workstation-vs-server).
Both source rows passed in 70.48 s, but sampled CPU rose to 242.65 s versus 193.25 s
in the earlier control; peak summed RSS was 5.22 GiB. That setting was reverted.

### Reference preparation inside the existing MSBuild worker

Reference preparation now invokes the real MSBuild `ResolveReferences` target
through a short-lived `BuildManager` inside the existing isolated analysis worker.
It retains the same project, configuration and selected framework and reads the
resolved-reference items from the completed project state. It uses one build node,
disables node reuse, restores the operating environment and shuts down the in-process
node after the build. Build diagnostics remain analysis failures, and cancellation
can cancel submissions. The outer worker process still owns the CLI cancellation
and timeout boundary, including child tasks. No graph, project output or resolved
reference result is cached by this change.

Matched Debug samples on CPUs 0–3 retained five existing generated-interceptor
and mixed-framework dispatch CLI rows, including every repeated CLI call. The
first pair ran control then candidate; the repeat reversed that order.

| Reference preparation | Wall seconds, first / repeat | Sampled CPU seconds, first / repeat | Peak summed RSS GiB, first / repeat |
|---|---:|---:|---:|
| Separate reference processes | 39.90 / 39.64 | 85.55 / 86.31 | 1.76 / 1.74 |
| Inside each analysis worker | 36.52 / 36.01 | 77.63 / 77.00 | 1.84 / 1.86 |

Median elapsed time fell 8.8% and sampled CPU about 10%, with a modest memory
increase. Both variants still launched 26 analysis workers; separate reference
processes fell from 26 to zero. All five rows passed in every sample. This is a
focused improvement, not evidence that the hosted workspace target is met.

Eight existing framework, referenced-generator, define and cache rows passed in
16.80 s; four imported-project/classification and file-local CLI rows passed in
28.55 s. Full raw graphs matched the original worker for mixed-framework Debug,
Release and changed-reference inputs. Temporary probes verified cancellation of
an active reference-build `Exec` child and preservation of failing-target
diagnostics. Probe sources were removed before committing. The solution built
without warnings or errors, and all 350 fast rows passed in 4.49 s including
complete invocation startup.

[CI run 36288612788](https://github.com/collinstevens/callrift/actions/runs/36288612788)
passed all fast, representative integration and quality jobs. All 26 workspace
rows passed in 136.31 / 267.55 / 226.56 s and all 25 routine case rows passed in
276.29 / 218.33 / 182.75 s on Ubuntu / Windows / macOS. Repository caches hit on
all three platforms. macOS passed all 305 scenarios in 473.88 s. The next
checkpoint superseded this run before Ubuntu and Windows scenarios completed;
their canceled jobs are not passing suite measurements. No completed per-suite
measurement met its target, and this partial run cannot establish the broad
dependency path on every platform.

### Reusing semantic graphs in the remaining CLI-heavy scenarios

Constructor equivalence keeps all seven workspace rows and all four
focused/unfocused, forward/reverse comparisons. Six rows now analyze each
revision once through the eligible real MSBuild fixture and reuse that graph pair.
The explicit-base row retains all four real CLI diffs, their exit-code checks and
JSON assertions. All seven fast counterparts remain unchanged.

Static initializer declarations retain all seven workspace rows. The four rows
with expected diagnostic codes retain their original CLI tree, reach, text and
Markdown commands, including diagnostic output checks. The three other rows now
reuse the existing semantic helper with real workspace analysis. Initializer and
sink labels, reachability, diagnostic expectations and both rendered formats are
still checked. Fast counterparts still use source analysis. No reviewed snapshot
or test selection changed.

Matched prepared Debug samples used CPUs 0–3, SDK
`11.0.100-rc.1.26425.128`, production revision `1710791`, fresh fixture-owned roots,
and the same 14 integration rows. TRX case-name multisets match exactly.

| Harness | Complete invocation seconds | Sampled CPU seconds | Peak summed RSS GiB |
|---|---:|---:|---:|
| Original repeated CLI analysis | 65.04 | 186.81 | 1.46 |
| Reused graph pairs | 38.43 | 62.03 | 1.90 |
| Reused graph pairs, repeat | 38.17 | 60.38 | 1.88 |

All rows passed in each run. Separate analysis workers fell from 84 to 24,
with two reusable fixture workers added; restores fell from 21 to eight and CLI
invocations from 56 to 20. The repeated candidate reduced elapsed time about 41%
and sampled CPU about 67%, with higher retained memory. These are focused local
measurements, not a claim that hosted suite targets are met.
All 350 fast rows passed through `mise run test:fast` in 5.37 s for the complete
invocation, including its incremental build and startup. The scenario project
also built without warnings or errors.

### Identity and presentation format reuse

The two file-local identity rows remain real workspace checks. Direct calls now
reuse an analyzed pair; interface dispatch retains all CLI diff and tree formats
and exit checks. The record-copy presentation row now checks all existing labels,
symbol identities, source locations, copy order and three command/format matrices
against one real workspace pair. The existing sealed-copy row still exercises
record-copy CLI diff formats, tree and reach routing. No independent fresh-process
determinism assertion was removed.

Both imported-project classification rows retain actual solution loading,
`Directory.Build.targets` imports, package restoration and test classification.
The excluded-tests row renders one fresh analyzed pair. The included-tests row
retains all three CLI formats, `--solution`, `--tests` and exit checks. The fixture
factory for explicit workspace options uses the original fresh workspace path;
these custom imports do not enter the eligible workspace pool. All fast rows and
all five integration row names remain unchanged.

The same prepared Debug, CPUs 0–3 setup measured the five existing integration
rows against `9caf37c`. Every row passed, and TRX case-name multisets match.

| Harness | Complete invocation seconds | Sampled CPU seconds | Peak summed RSS GiB |
|---|---:|---:|---:|
| Repeated CLI analysis | 35.40 | 122.67 | 2.21 |
| Reused semantic graphs | 22.11 | 64.26 | 2.31 |
| Reused semantic graphs, repeat | 22.31 | 65.07 | 2.30 |

Analysis workers fell from 42 to 17, with two reusable fixture workers added;
restores fell from 11 to eight and CLI invocations from 27 to nine. Median elapsed
time fell about 37% and sampled CPU about 47%, with slightly higher retained RSS.
All 350 fast rows passed through the normal mise command in 5.29 s, and the
scenario project built without warnings or errors.

### Workspace queries share analyzed inputs

Framework dispatch retains all four framework/nesting rows and every diff, tree
and reach assertion. Three rows reuse fresh MSBuild graphs for their before/after
projects; the non-nested `netstandard2.0` row retains all three real CLI commands
and exit checks. Both target frameworks and both nested-type modes still resolve
real project references and assert the referenced project's scoped identities.

The generated-interceptor row still runs its diff twice in separate CLI processes
and compares the complete outputs, preserving the regenerated-name determinism
boundary. Its six tree/reach rendering assertions now reuse one additional fresh
workspace graph, with an explicit empty-diagnostics check. The generator project
is still restored and built from source. All three formats retain their existing
positive and fallback-exclusion assertions. CLI query format routing and exit
behavior remain independently covered by `QueryTests.Query`; workspace CLI query
routing also remains in the framework-dispatch representative.

Matched prepared Debug samples on CPUs 0–3 retained the same five existing rows
and exact TRX case-name multisets. All passed in each invocation.

| Harness | Complete invocation seconds | Sampled CPU seconds | Peak summed RSS GiB |
|---|---:|---:|---:|
| Repeated CLI queries | 36.42 | 77.96 | 1.82 |
| Reused query graphs | 28.48 | 48.85 | 1.84 |
| Reused query graphs, repeat | 28.20 | 48.50 | 1.83 |

Median elapsed time fell about 22% and sampled CPU about 38%. Analysis workers
fell from 26 to 15 and CLI invocations from 20 to five. Restores increased from
10 to 11 because the shared generated graph deliberately uses a fresh workspace.
The workspace project built without warnings or errors. Hosted targets remain
unproven for this checkpoint.

### Sharing the bounded fixture pool with workspace assertions

Workspace snapshots and the non-CLI framework-dispatch rows now use the same
strict project-shape eligibility checks and four-slot worker pool as scenarios.
The pool accepts explicit MSBuild options, which remain part of the shape key.
Eligible single-project snapshots can retain their loaded workspace and refresh
source documents; eligible project-reference fixtures reuse only the worker and
still create and restore fresh roots for both revisions. Custom imports,
packages and referenced generators retain the original fresh-worker path.
The existing SDK regex snapshot still exercises changed generated output through
real MSBuild and compilation. CLI parity, cache behavior and independent generated
interceptor runs retain their original process boundaries.

Both test assemblies now register the shared assembly runner explicitly. It
disposes each assembly's pool after its cases finish. The workspace project links
the existing fixture implementation and copies the dedicated worker as a build
dependency; no new tests, project-shape exceptions or concurrency increases were
introduced. Locked restore succeeded without lock-file changes, and the complete
solution built without warnings or errors.

The complete 26-row workspace suite ran on the prepared Debug host, CPUs 0–3,
against `e2f9867`. The repeat reversed the candidate/control order. All rows passed
in all four samples; exact TRX case-name multisets and reviewed snapshots match.

| Workspace harness | Invocation seconds, first / repeat | Sampled CPU seconds, first / repeat | Peak summed RSS GiB, first / repeat |
|---|---:|---:|---:|
| Fresh workers | 66.29 / 66.09 | 244.21 / 245.32 | 2.73 / 2.75 |
| Shared bounded pool | 62.24 / 61.84 | 228.20 / 227.27 | 3.37 / 3.24 |

Median elapsed time fell 6.3% and sampled CPU 7.0%. Analysis workers fell from 72
to 54, with two pooled workers observed; restores fell from 56 to 48. Both variants
had four overlapping cases and 25–26 sampled CLI processes, reflecting the
sampler's limited visibility into short-lived processes. The pool was fully
disposed before each invocation returned. Higher retained memory is the explicit
cost of this modest gain; this local measurement does not establish hosted targets.
All 350 fast rows passed through mise in 5.78 s with no fixture workers started.
The seven constructor-equivalence integration rows passed through the standard
focused command in 13.45 s including build/startup, confirming scenario pool use
and teardown after moving assembly registration. No fixture worker remained alive.

### Matched complete E2E results at 2fae0fb

Both optimized `mise run test:e2e` invocations passed all 426 executions: 305
scenarios, 26 workspaces and all 95 pinned cases. The original `75bdd97` baseline
and candidate used the same Ubuntu 26.04 Ryzen 9 7945HX host, 44,825 MiB RAM,
native 32-logical-CPU affinity, SDK `11.0.100-rc.1.26425.128` and Debug configuration.
SDK and global NuGet caches were prepared. Each version's cold run started with
absent external repository/workspace caches; its warm run retained those same
directories. Optimized explicit preparation took 1.20 s to restore and 1.46 s to
build, outside the measured command. No other local build or profile overlapped.

| Complete command | Original cold / warm | Optimized cold / warm |
|---|---:|---:|
| Elapsed seconds | 3269.90 / 3238.36 | 508.23 / 474.53 |
| Sampled descendant CPU seconds | 15532.82 / 15511.52 | 2394.24 / 2367.18 |
| Peak summed RSS GiB | 16.60 / 16.47 | 8.65 / 11.88 |
| Analysis workers | 1014 / 1014 | 194 / 194 |
| Additional pooled fixture workers | 0 / 0 | 6 / 6 |
| Sampled restore processes | 805 / 805 | 169 / 171 |

Cold elapsed time fell 84.5%, warm elapsed time 85.3%, and sampled CPU about 84.6%
and 84.7%. RSS is summed across sampled descendants, not unique physical memory;
detached compiler-server CPU remains outside this historical sampler. The warm
RSS increase relative to the candidate's cold run shows why a single peak should
not be treated as a guaranteed memory ceiling.

The optimized scenario/workspace/all-case dotnet portions were
97.23 / 38.41 / 372.23 s cold and 97.19 / 39.60 / 337.38 s warm. Observed case
overlap was four / four / two. TRX method-and-parameter multisets match the
baseline exactly; scenario full names also match, while earlier workspace and
case class moves are mapped separately. The pinned manifest and every reviewed
snapshot are unchanged from `90083f5`. These full-corpus results are independent
of the smaller routine CI selection.

### Supported-platform CI at 2fae0fb

[Run 36289779318](https://github.com/collinstevens/callrift/actions/runs/36289779318)
passed every required job on all three platforms. All case jobs restored their
repository caches; each platform retained 305 scenarios, 26 workspaces and 25
routine cases, alongside fast and representative integration coverage.

| Complete dotnet test command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios | 382.18 s | 452.52 s | 364.18 s |
| Workspaces | 196.21 s | 230.05 s | 188.59 s |
| Routine cases | 238.34 s | 353.17 s | 348.01 s |
| Broad dependency path including setup | 7m 05s | 9m 01s | 6m 46s |

Initial scheduling delay was 40 / 41 / 44 s respectively, reported separately
from those paths. The nine broad jobs consumed 54.87 runner-minutes, versus
143.82 for the original three sequential broad jobs. Thus the lower latency is
accompanied by less aggregate work despite repeated setup. All warm broad paths
met ten minutes in this run, but none of its individual suite measurements met
their targets. Optimized cold CI evidence remains outstanding. Hosted variance
and the remaining expensive diagnostic, interceptor and large-source cases mean
the performance goal is still open.

A subsequent call-collection experiment reused receiver facts within each call
and skipped empty containing-type binding descriptions. All 350 fast rows passed,
the two largest source-roots cases passed in every sample, and 132 complete
scenario graphs matched the original, including body fingerprints and nested
generic bindings. The performance gain did not repeat: control/candidate elapsed
times were 83.47 / 79.32 s initially, then 78.95 / 84.10 s with execution order
reversed. Sampled CPU was 247.70 / 234.57 s initially and 232.93 / 244.20 s on
repeat. Median elapsed time was slightly worse and median CPU essentially flat.
Both code changes were reverted, original binaries rebuilt, and temporary probe
sources removed. No improvement is claimed from that experiment.

### Callback formats and repeated invalid-declaration coverage

The method-group callback row retains real CLI text, Markdown and JSON commands,
including exit-code assertions. Its inline-lambda counterpart now analyzes one
real workspace revision pair and renders the same three formats. The repeated
callback-location row also reuses a workspace pair; its two exact line-location
assertions and no-collapse assertion remain unchanged. `QueryTests.Query`'s
`diff-locs` row independently retains CLI `--locs`, all three formats and the
reviewed exit-code/output snapshot.

The missing-partial initializer row now uses the existing workspace graph helper.
It still checks the unavailable initializer, absence of invented sink calls,
`unresolved-static-initializer` JSON diagnostic, reachability and both readable
formats. The duplicate-class row retains CLI coverage of that same diagnostic
code in JSON and text/Markdown stderr. Extern and duplicate-constructor rows
retain their distinct diagnostic codes and all original CLI assertions. No
production code, snapshots or test selections changed.

On the same prepared Ubuntu host and SDK, Debug, CPUs 0–3, ten affected workspace
rows took 39.14 s before and 29.91 / 29.82 s after. Every row passed in every
sample, with identical TRX case-name multisets. Sampled CPU fell from 80.14 s to
50.20 / 49.22 s; peak summed RSS rose from 1.62 GiB to 1.81 / 1.83 GiB. CLI
processes fell from 24 to 15, analysis workers from 32 to 18, and restores from
11 to seven. Pooled fixture workers increased from one to two. Fixture roots
were fresh per invocation, SDK/NuGet caches prepared, and no other local build
or profile overlapped. All 350 fast rows passed in 5.44 s including invocation
and build startup.

A separate production experiment shared semantic models among each type's
initializers. The two large source-roots cases passed, but complete elapsed time
worsened from 80.63 to 82.66 s, CPU from 236.34 to 246.80 s, and peak summed RSS
from 5.33 to 6.11 GiB. It was reverted without further measurement; no benefit
is claimed. The temporary reflection probe also showed that Roslyn 5.9's
nullable-disabled semantic-model API remains experimental. Nullable analysis
was not disabled, and temporary probe sources were removed.

### Same-source CI repeat at 2916a29

[Run 36291136914](https://github.com/collinstevens/callrift/actions/runs/36291136914)
passed every required job, using the same production and test source as
`2fae0fb`. Repository caches hit on all platforms. Complete dotnet command times
were:

| Suite | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios, 305 rows | 277.47 s | 513.38 s | 359.48 s |
| Workspaces, 26 rows | 196.07 s | 162.30 s | 162.66 s |
| Routine cases, 25 rows | 264.94 s | 343.64 s | 294.24 s |
| Broad path including setup | 5m 16s | 10m 21s | 6m 48s |

Initial queue delay was 3 / 3 / 7 s, separate from those paths. Broad jobs used
52.18 runner-minutes. Ubuntu and macOS scenarios met six minutes in this run;
Windows scenarios and all workspace/case targets remained unmet. Windows also
exceeded the broad target by 21 seconds. The earlier all-platform warm result
therefore is not evidence of stable target compliance. Optimized cold CI and
further reductions remain necessary.


### Callback checkpoint CI at 1ab0509

[Run 36291797116](https://github.com/collinstevens/callrift/actions/runs/36291797116)
passed every required job with repository-cache hits on all platforms and all
305 / 26 / 25 broad executions per OS. Complete scenario commands took
267.09 / 435.41 / 476.73 s on Ubuntu / Windows / macOS; workspaces took
198.79 / 167.40 / 163.93 s, and routine cases 265.21 / 278.04 / 311.78 s.
Broad paths including setup were 5m 12s / 8m 53s / 9m 06s, with initial queue
waits of 2 / 2 / 7 s reported separately. The nine broad jobs used 53.80
runner-minutes. All broad paths met ten minutes in this run, but only Ubuntu
scenarios met its individual suite target. Platform variation still precludes
claiming stable compliance or attributing each hosted timing change solely to
the callback harness adjustment.


### Balanced broad CI shards

Each supported OS now runs A and B shards for each of Scenarios, Workspaces and
Cases. The partition keeps whole classes and repository groups together.
`Test-ShardA.json` lists A's classes; B is the exact complement, so new classes
remain selected. The script intersects the partition with any caller filter and
keeps the zero-execution failure. Shards are rejected for Fast, Integration and
E2E; their existing local entrypoints retain their selections. Reproduction
commands, TRX names and failure-artifact names identify the shard. No additional
artifact payloads are uploaded.

The final split is 125 + 180 scenarios, 13 + 13 workspace rows and 12 + 13
routine cases. Four existing workspace rows moved classes without changing their
assertions: the two netstandard2.1 framework-dispatch variants now belong to
`Framework21DispatchTests`, and the changed explicit/implicit interceptor rows
to `ChangedExplicitInterceptorTests` / `ChangedImplicitInterceptorTests`. The
former classes retain the other rows. All graph, scope, diagnostic, generated
identity, repeat-process, query and format checks remain. These cases use
independent roots and no shared mutable class fixture. Splitting the prior
framework/interceptor collections removes a long serial tail while preserving
the four-case assembly limit. The environment-mutating scenario collection
remains exclusive; real-world classes retain their two-case limit and shared
repository/graph ownership.

This changes nine broad jobs to eighteen. It buys latency with additional runner
capacity and repeated setup; it is not presented as a reduction in useful test
work. Earlier graph/restore/worker reductions remain the source of the measured
work savings against the original 143.82-runner-minute baseline. Final acceptance
requires hosted cold/warm paths, each suite's last shard completion, aggregate
command time, all eighteen job durations and queue delay to be reported together.
Local sequential shard samples do not establish hosted parallel latency.

Case caches include OS, shard, manifest and partition configuration in the key.
Separate keys prevent competing shards from publishing an incomplete shared
cache on their first run. Both Serilog classes stay in the same shard. The new
keys intentionally create a measurable first cold preparation; a subsequent run
must confirm cache hits before it is called warm. Existing cache entries are
preserved, and the pinned corpus and license checks are unchanged.


The local comparison uses the prepared Ubuntu 26.04 Ryzen 9 7945HX host,
44,825 MiB RAM, SDK `11.0.100-rc.1.26425.128`, Debug, CPUs 0–3, fresh fixture
roots and prepared SDK/NuGet caches. Routine cases reuse the same external
repository/workspace caches as their control. Every complete command includes
PowerShell and dotnet startup and uses the prepared build. Shards ran sequentially
with no overlapping local build/profile, so their maximum time is a scheduling
estimate, not a measured concurrent CI result.

| Suite | Unsplit control seconds | A / B seconds | Control / summed shard CPU seconds |
|---|---:|---:|---:|
| Scenarios, 305 | 122.54 | 68.22 / 75.40 | 426.44 / 476.64 |
| Workspaces, 26 | 60.53 | 36.25 / 30.80 | 228.55 / 234.44 |
| Routine cases, 25 | 96.24 | 58.16 / 58.77 | 290.83 / 351.98 |

Thus scenario shards add 17.2% combined elapsed time and 11.8% sampled CPU;
workspace shards add 10.8% elapsed time and 2.6% CPU, and case shards add 21.5%
elapsed time and 21.0% CPU. These costs must remain visible
when judging the lower longest-shard time. Scenario peak summed RSS was 4.36 GiB
in the control and 3.74 / 3.60 GiB per shard; case peaks were 4.70 GiB versus
4.17 / 4.20 GiB. Workspace peaks were 3.22 GiB versus 3.31 / 3.26 GiB.
Peak RSS is per invocation; the CI shards execute on separate runners.
Sampled case workers/restores remain exactly fourteen /
ten in aggregate, with all repository views kept together.

A trial that scheduled the largest routine cases first made the case shards
slower: 59.41 / 72.53 s and 190.11 / 221.47 CPU-seconds. Restoring the original
collection order reduced the second shard to 58.77 s and 156.57 CPU-seconds.
That trial was reverted; the all-cases and routine collection ordering are
unchanged. The remaining split-process overhead is reported rather than hidden.

All 305 scenario names and 25 routine-case names exactly match the disjoint
union of their two shards. Workspace method-and-parameter identity is retained
for all 26 rows, with the four class moves explicitly mapped. Empty/invalid
filters, unsupported shard suites and unknown shard names retain failure
behavior. Hosted results, new cache saves and a subsequent cache-hit run remain
pending at this checkpoint.

Across the three routine suites, sampled analysis-worker counts stay at 150;
pooled fixture workers increase from six to twelve and sampled restores from
133 to 136. This exposes the extra process/setup work alongside the CPU and
elapsed overhead. The unsharded workspace control uses the same reorganized
classes and script as its shard samples, so its 60.53 s is directly comparable.

The final fast command passed all 350 rows in 5.33 s including startup/build.
Workflow lint passed. Four invalid-argument probes and a disjoint shard/filter
intersection all failed as intended; no temporary tests were left in the tree.
The solution build completed without warnings or errors. Reviewed snapshots and
the pinned manifest remain unchanged.


### Current isolation and cleanup accounting

The latest six local shard TRX files reached four overlapping scenario/workspace
rows and two real-world rows, never more. The `PartialCloneTests` rows did not
overlap any other row in their assembly. All 43 temporary fixture roots visible
in the process samples were gone afterward, and no fixture or analysis workers
remained alive. External repository/workspace benchmark caches remain owned by
the caller and deliberately persist between cold and warm measurements.

| Remaining serial boundary | Isolation or resource reason |
|---|---|
| `ProcessEnvironmentCollection` | Partial-clone cases mutate parent `GIT_TRACE2_EVENT` and `GIT_NO_LAZY_FETCH`; exclusive execution prevents unrelated Git calls from observing those values. |
| Repository preparation lease | A per-cache-path exclusive file handle protects clone publication, missing-object fetches and pinned remote/license validation. Analysis runs after releasing it. Cold clones publish from owned staging directories atomically. |
| Production workspace cache entry | An exclusive file handle covers source materialization, restore/build outputs and analysis for one content/target/framework/configuration identity. Those files cannot be rewritten by another request while MSBuild reads them. Independent identities proceed concurrently. |
| One pooled fixture worker's requests and revision pair | A leased process owns one retained workspace and root. Rewriting before/after source and refreshing its immutable solution must happen in sequence. At most four leases exist; failed workers are discarded. |
| Real-world rows within a repository class | `RealWorldCaseFixture` owns one mutable cached graph pair keyed by repository, revisions, full MSBuild options and test inclusion. Serial rows safely reuse that pair across formats/views without multiplying large retained graphs. Repository groups stay in one shard. |
| Scenario/workspace collection scheduling | Four active collections bound nested restores, workers and Roslyn analysis. Rows within each scheduled collection run one at a time under that resource budget; long independent families were split into additional collections without increasing the limit. Real-world scheduling uses the measured two-collection budget. |

Fixture source/project/options changes remain covered by the retained-workspace
probes recorded earlier and the existing real project/framework/generator cases.
Unknown project shapes use fresh workers. Three-minute fixture requests, EOF,
parent exit and assembly teardown keep their previous ownership rules; the
existing sweep cases still exercise timeout/cancellation and descendant-process
termination. The new shard partition changes scheduling and cache namespaces,
not those lifecycle rules. The one/two/four comparisons and their memory/CPU
tradeoffs remain recorded above rather than inferred from core count alone.


### First cold shard CI at a3bba0f

[Run 36292855750](https://github.com/collinstevens/callrift/actions/runs/36292855750)
passed every required job. Each platform retained all 350 fast, 305 scenario,
26 workspace and 25 routine-case executions, plus representative integration.
All six case jobs explicitly missed their new repository-cache keys and then
saved them successfully. Invocation SDK and configuration remained
`11.0.100-rc.1.26425.128`, Debug. Hosted images were Ubuntu 24.04.5
(`ubuntu-24.04`, image `20260920.314.1`), Windows Server 2025
(`windows-2025-vs2026`, image `20260922.246.2`) and macOS 26.6.2 arm64
(`macos-26-arm64`, image `20260907.0351.1`).

| Complete dotnet command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios A, 125 rows | 204.09 s | 253.60 s | 156.81 s |
| Scenarios B, 180 rows | 177.03 s | 230.61 s | 204.07 s |
| Workspaces A, 13 rows | 118.12 s | 129.59 s | 99.12 s |
| Workspaces B, 13 rows | 100.46 s | 112.67 s | 72.93 s |
| Cold cases A, 12 rows | 139.57 s | 195.88 s | 175.97 s |
| Cold cases B, 13 rows | 149.07 s | 188.48 s | 164.35 s |
| Whole broad feedback path including setup/staggered starts | 4m 28s | 5m 50s | 6m 46s |

Initial queue delay before the first broad job was 2 / 2 / 7 s. The last broad
job started 54 / 4 / 172 s after the first on its platform. The broad paths above
include those later starts; they are not just the largest individual command.
First-test-step-start to last-test-step-finish spans were 232 / 256 / 261 s for
scenarios, 121 / 132 / 139 s for workspaces and 192 / 216 / 347 s for cases.
These spans expose setup/scheduling skew that per-command maxima alone omit.

All eighteen broad jobs used 67.90 runner-minutes, 52.8% below the original
143.82-minute cold baseline. Aggregate dotnet command time was 47.87 minutes.
Runner time was 14.10 minutes higher than the preceding warm nine-job run;
that difference includes additional setup, cold repository preparation and
host variation, so it is not a pure estimate of shard overhead. The matched
local overhead measurements above remain the controlled comparison.

Cold broad feedback meets ten minutes on every OS. Windows workspace A still
exceeds its two-minute command budget by 9.59 s, and warm case behavior has not
yet been measured for the new cache layout. Test-step spans and queue costs also
remain visible. The phase is not complete.

### Removing the remaining mixed-framework class tail

Windows workspace A spent 116.79 accumulated seconds in the four mixed-framework
rows, versus 103.68 s in framework dispatch. The three project/solution and
multi-target variants now move to `SolutionFrameworkWorkspaceTests`,
`MultiTargetFrameworkWorkspaceTests` and
`MultiTargetSolutionFrameworkWorkspaceTests`; the original class keeps the
single-target-library/project row. All four delegate to the same unchanged
assertion body, retain real CLI invocations and remain in workspace shard A.
They use independent repositories/workspace roots. The collection limit stays
four, and the row total stays thirteen in A and twenty-six across both shards.

The affected thirteen-row command passed in 35.44 s, with 119.73 sampled
CPU-seconds and 3.33 GiB peak summed RSS, against 36.25 s / 123.07 CPU-seconds /
3.31 GiB before. This small local difference does not establish a hosted gain;
the change removes the measured serial boundary for the next CI run. Exact
method/parameter multisets match, with three additional class moves recorded.
The solution build passed without warnings or errors. The unchanged fast tier's
latest 350-row validation remains 5.33 s.

Adding the class names changes the partition-file cache hash even though case
membership is unchanged. The next case jobs therefore prepare another cold
namespace; final warm evidence still requires a following cache-hit run. Old
cache entries are preserved.

### Cold confirmation at 3060e3e

[Run 36293540835](https://github.com/collinstevens/callrift/actions/runs/36293540835)
passed all required jobs with the same SDK, Debug configuration and hosted image
versions as the preceding run. Counts remain 350 fast, 305 scenarios, 26
workspaces and 25 routine cases per OS, plus representative integration. All six
case jobs explicitly missed and saved the new partition-hash cache namespace.

| Complete dotnet command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios A / B | 220.13 / 190.46 s | 254.13 / 220.83 s | 189.76 / 139.25 s |
| Workspaces A / B | 112.74 / 101.02 s | 96.78 / 115.57 s | 60.89 / 88.82 s |
| Cold cases A / B | 157.97 / 113.54 s | 194.11 / 195.95 s | 118.83 / 135.91 s |
| Whole broad path including setup/staggered starts | 4m 18s | 5m 48s | 5m 23s |

Initial queue delay was 1 / 1 / 5 s; the last broad job started 1 / 50 / 163 s
after its platform's first job. First-test-step-start to last-test-step-finish
spans were 221 / 256 / 247 s for scenarios, 117 / 179 / 196 s for workspaces,
and 160 / 248 / 269 s for cold cases. Thus every workspace command meets two
minutes, while the Windows/macOS combined spans still exceed that duration
because of setup and scheduling skew. These are distinct measurements.

The eighteen broad jobs consumed 62.57 runner-minutes, 56.5% below the original
cold baseline, with 45.11 aggregate dotnet command minutes. All broad paths
again meet ten minutes. Warm repository-cache behavior still needs a following
run with unchanged partition configuration; the cold Windows cases are not
presented as meeting the warm three-minute target.

### Warm repository-cache confirmation at 7faf551

[Run 36293916171](https://github.com/collinstevens/callrift/actions/runs/36293916171)
passed all required jobs with unchanged source, selection and partition keys.
All six case jobs explicitly restored their repository caches. Every platform
retained 350 fast, 305 scenario, 26 workspace and 25 routine-case executions.

| Complete dotnet command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios A / B | 202.93 / 137.18 s | 145.91 / 169.41 s | 196.56 / 199.97 s |
| Workspaces A / B | 78.89 / 110.97 s | 132.74 / 83.72 s | 101.00 / 65.44 s |
| Warm cases A / B | 148.42 / 144.88 s | 188.89 / 154.76 s | 113.25 / 127.15 s |
| Whole broad path including setup/staggered starts | 4m 03s | 4m 56s | 4m 34s |

Initial queue delays were 1 / 1 / 5 s, and last-start delays 52 / 3 / 117 s.
First-test-step-start to last-test-step-finish spans were 208 / 208 / 217 s for
scenarios, 119 / 148 / 116 s for workspaces and 200 / 191 / 235 s for cases.
The eighteen jobs used 61.08 runner-minutes and 41.70 aggregate command minutes,
versus 62.57 / 45.11 in the preceding cold run. These hosted observations include
machine and scheduling variance; they are not a controlled estimate of cache
savings. Broad feedback meets ten minutes in both conditions. Windows workspace
A and case A still miss their command budgets, and suite spans retain queue/setup
skew. The goal remains active.

### Measuring package-cache reuse

The next workflow checkpoint caches NuGet global packages for workspace and case
shards. Repository-cache hits alone do not retain fixture dependencies on fresh
runners. The package key includes OS, suite, shard, lockfiles, SDK configuration,
pinned corpus, partition and fixture sources. Separate shard keys avoid a first
writer publishing only another shard's package set. Fresh project restoration
and all analysis still execute; only package acquisition can be reused.

The cache stores `~/.nuget/packages`, following the
[GitHub dependency-cache guidance](https://docs.github.com/en/actions/tutorials/build-and-test-code/net).
There are twelve package entries per configuration. Download, extraction, save
time and cache size must be counted alongside command time before retaining this
change as a measured improvement. The first run populates this package namespace;
a following unchanged-key run must demonstrate explicit hits. No performance
benefit is claimed yet, and failure artifact payloads remain unchanged.

[Population run 36294268859](https://github.com/collinstevens/callrift/actions/runs/36294268859)
at `5a2814e` passed all required jobs. Every repository cache hit, and all twelve
new package keys explicitly missed and then saved. Package saves took 3–14 s
per entry in the job-step timestamps; those costs are included below.

| Complete dotnet command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios A / B | 201.86 / 188.68 s | 267.56 / 213.10 s | 214.02 / 131.01 s |
| Workspaces A / B | 113.04 / 100.83 s | 142.99 / 115.56 s | 120.40 / 106.08 s |
| Warm repository, cold package cases A / B | 146.50 / 149.46 s | 218.75 / 167.09 s | 121.97 / 104.05 s |
| Whole broad path including setup/staggered starts | 3m 57s | 6m 23s | 7m 20s |

Initial queue delays were 2 / 2 / 6 s, last-start delays 43 / 48 / 162 s.
First-test-step-start to last-test-step-finish spans were 204 / 274 / 340 s for
scenarios, 115 / 172 / 129 s for workspaces and 188 / 284 / 124 s for cases.
The eighteen jobs consumed 67.12 runner-minutes and 47.05 aggregate command
minutes. Windows workspace/case A and macOS workspace A miss their command
budgets. This run populates the package cache; it cannot establish the benefit
of restoring it. The following documentation-only checkpoint keeps all code,
partition and package keys unchanged for that comparison.

[Cache-hit run 36294683341](https://github.com/collinstevens/callrift/actions/runs/36294683341)
at `d5783e5` passed all required jobs and explicitly restored all six repository
and twelve package caches. Package entries were 132–329 MiB compressed; restoring
and extracting them took 2–15 s each, 76 s in aggregate. Their combined compressed
size was 2.40 GiB. This is a measurable transfer/storage cost, not free setup.

| Complete dotnet command | Ubuntu | Windows | macOS |
|---|---:|---:|---:|
| Scenarios A / B | 207.77 / 192.06 s | 255.77 / 207.39 s | 130.41 / 121.77 s |
| Workspaces A / B | 111.03 / 53.42 s | 136.66 / 116.46 s | 66.40 / 102.78 s |
| Warm cases A / B | 159.15 / 137.37 s | 167.15 / 177.06 s | 160.52 / 210.59 s |
| Whole broad path including setup/staggered starts | 4m 35s | 5m 43s | 6m 53s |

Initial queue delays were 2 / 2 / 6 s, last-start delays 46 / 1 / 177 s.
First-test-step-start to last-test-step-finish spans were 233 / 258 / 132 s for
scenarios, 114 / 145 / 222 s for workspaces and 171 / 184 / 371 s for cases.
Broad runner time was 63.40 minutes and aggregate command time 45.23 minutes.
This is below the population run's 67.12 / 47.05 minutes, but hosted variance
prevents attributing the entire difference to caching. Package hits do not solve
the Windows workspace A or macOS case B targets. No suite or coverage change was
made between these two runs.

### Additional workspace and case scheduling headroom

Windows workspace A still spent 110.88 accumulated seconds in its two framework
dispatch rows. The nested netstandard2.0 row now moves to
`NestedFrameworkDispatchTests` while preserving its shared assertion body, so it
can overlap the retained real-CLI row within the same four-collection limit.
Three workspace shards distribute the existing 26 rows as 8 / 12 / 6. There is
no production analysis change and no new test row.

macOS case B's ASP.NET Core row took 145.91 s but finished in a 210.59 s command
because earlier collections delayed its start. Three case shards keep ASP.NET
Core with the process checks, Autofac with both Serilog classes, and the remaining
repositories together. Each repository's views still share one owning class and
graph cache. The per-process case limit remains two. Scenario partitions stay
unchanged at 125 / 180.

`Test-Shards.json` records explicit groups and exactly one `null` complement per
suite. The script rejects an unconfigured shard, intersects caller filters and
still fails an empty selection. The complement keeps future classes selected.
This raises broad jobs from eighteen to twenty-four, so additional setup,
package/repository cache entries and scheduling delay must remain visible.
Partition/source hashes invalidate the corresponding cache namespaces; final
acceptance requires new cold and cache-hit hosted evidence.

Local samples used the same prepared Ubuntu 26.04 host, SDK, Debug build and
four-CPU affinity as the preceding shard comparison. Commands ran sequentially;
routine cases reused the same external repository/workspace caches. Complete
invocations include mise, PowerShell and dotnet startup.

| Three-shard sample | A | B | C |
|---|---:|---:|---:|
| Workspace rows | 8 | 12 | 6 |
| Workspace seconds | 26.29 | 24.65 | 20.88 |
| Workspace sampled CPU seconds | 78.59 | 89.39 | 72.75 |
| Workspace peak summed RSS GiB | 3.04 | 2.85 | 3.45 |
| Routine-case rows | 5 | 10 | 10 |
| Routine-case seconds | 57.05 | 34.05 | 48.36 |
| Routine-case sampled CPU seconds | 144.98 | 118.89 | 150.94 |
| Routine-case peak summed RSS GiB | 4.16 | 2.35 | 4.20 |

Workspace combined elapsed time is 71.82 s and CPU 240.73 s, versus the two-shard
66.25 s / 231.10 s after mixed-framework splitting. Case combined elapsed time
is 139.46 s and CPU 414.81 s, versus 116.93 s / 351.98 s with two shards:
19.3% more elapsed work and 17.9% more CPU. The local longest case command only
falls from 58.77 to 57.05 s; this is not claimed as a substantial throughput gain.
The hosted hypothesis is that isolating the large source-only repository removes
its delayed start and competing analysis. New CI evidence must justify that cost.

The workspace union exactly preserves all 26 method/parameter rows, with the one
additional class move mapped. Routine cases exactly preserve all 25 full names;
both unions are disjoint. Scenario A membership is byte-for-byte equivalent as
parsed JSON and B remains its complement. Observed overlap was four per workspace
shard and two per case shard. All 22 sampled owned roots were deleted and no
fixture/analysis worker remained alive. Four invalid/empty-selection probes and
workflow lint passed. The solution build had no warnings or errors; snapshots
and pinned inputs remain unchanged.

The subsequent `mise run test:fast` passed all 350 rows; its dotnet portion,
including build/restore, took 4.31 s. The complete wrapper's earlier measured
5.33 s remains the latest full-process fast timing, rather than substituting
this narrower timer for it.
