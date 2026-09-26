# Development performance inventory

This work tracks `WORKFLOW_DEVEX_GOAL.md`; it does not resume `GOAL.md`.
The inventory describes existing assertions, not permission to remove integration
coverage. The ordinary semantic migration is complete; terminal broad CI results
remain under review. Unsampled classes have no measured cost rank.

## Current results

| Evidence | Result |
|---|---|
| Latest prepared fast command, `9e7a117` | 350 passed in 4.58 s including changed-code build, mise and PowerShell |
| Matched complete scenario runs | The same 655 names passed in 460.06 s with default ordering and 448.36 s with prioritized ordering |
| Fresh checkout, warm SDK/package caches | Explicit restore 1.07 s; installed commit hooks 3.79 s; first fast command and build 4.76 s |
| Supported-platform feedback, `9e7a117` | Fast, representative integration/packaging and quality passed; broad verification remains running |
| Preservation | 681 scenario/workspace executions retained; reviewed snapshots and production code unchanged from `56e12bf` |

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
Fast-tier scope is not yet final; remaining semantic candidates and real boundaries
must be distinguished before assessing the meaningful-coverage target.

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

## Current supported-platform evidence

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
ten-minute feedback target. The three broad verification jobs remain active and
are not reported as passed. Their terminal results are the remaining completion
evidence. Documentation-only evidence checkpoints use `[skip ci]` to preserve the
ongoing validation of unchanged source rather than cancel it with an identical run.

## Explicit feedback commands and hook sample

The command/workflow checkpoint (parent `c94cdb5` plus the changes in this commit)
adds the already-direct cancellation, precancelled Git and ordinary generic-context
boundary classes to the fast selection. Discovery proves 156 fast rows plus 483
non-fast rows equals the original 639, with no overlap. The three costly context
budget rows remain in the slow selection; they are not dropped. Migration of the
remaining semantic families is still required before claiming the 80% design target.

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
claim. Hooks performed no build, restore or test execution. Final fast-tier scope
and cross-platform results remain incomplete, so this sample does not authorize
silently adding tests to hooks.

CI now has independent three-OS fast and representative integration jobs plus a
formatting/workflow job. The representative job covers real Git hydration, invalid
CLI input, index/working tree, workspace parity, project references, a generator,
restored-cache/framework errors and installed packaging. It has a ten-minute timeout;
a timeout is failure, not evidence of meeting the target. Broad non-fast scenarios,
workspaces and routine real-world cases still run sequentially in a separate OS
matrix. Scheduled/manual all-case workflows and release validation remain intact.
New pushes cancel superseded runs on the same workflow/ref; cancellation remains
visible and is never counted as success. The first platform timings are recorded below.

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

## Isolation and remaining completion evidence

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
remain in force. Current-platform CI for the final relocated selection and terminal
broad-suite results still need inspection before the workstream is complete.
