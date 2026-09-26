# Test-suite performance goal

Status: second performance phase in progress; local profiling and real-world format reuse underway. The previous phase established fast everyday feedback, but broad CI still takes 46–49 minutes. This phase makes the remaining suite fast. It does not resume the paused implementation goal in `GOAL.md`.

## Outcome

Bring broad CI feedback below ten minutes on every supported operating system while preserving behavioral coverage, reviewed expectations, and real integration boundaries. Reduce the work performed as well as elapsed time: moving tests to another trigger, shrinking the selection, or adding runners alone does not achieve this goal.

Keep the existing fast development path fast. Broad tests remain asynchronous CI feedback and explicit local commands, never a commit or push gate. Completion requires measured improvement in the expensive suites themselves, not another declaration that slow CI is acceptable because local iteration is unblocked.

The previous phase's inventory and measurements remain historical evidence in [development-performance.md](docs/development-performance.md). Its completed checklist does not establish completion of this phase.

## Measured baseline

Source revision `90083f5`, pinned SDK `11.0.100-rc.1.26425.128`, Debug, [CI run 36257230296](https://github.com/collinstevens/callrift/actions/runs/36257230296). All supported platforms passed. Test commands use the prepared build; suite durations below include test-command startup.

| Broad CI stage | Executions per OS | Ubuntu | Windows | macOS |
|---|---:|---:|---:|---:|
| Slow scenarios | 305 | 26m 53s | 22m 39s | 22m 24s |
| Workspace integration | 26 | 8m 49s | 9m 06s | 8m 51s |
| Routine real-world and process cases | 25 | 12m 03s | 15m 43s | 14m 02s |
| Setup, build, and remaining overhead | — | ~48s | ~95s | ~56s |
| Total broad job | 356 | 48m 33s | 49m 02s | 46m 14s |

The three suites run sequentially within each OS job; OS jobs run concurrently. Tests account for about 97–98% of each broad job. Repository caches missed on all three platforms. The logs do not separate repository preparation, network waits, restore, project loading, and analysis time.

The independent fast tier contains 350 executions per OS. Together with broad verification, the baseline is 706 executions per OS. Representative integration and packaging jobs finish in about two to four minutes. CI selects `CALLRIFT_CASE_SET=routine`; the default local `test:e2e` includes the larger all-cases corpus. Do not present routine CI timing as a measurement of that full local command.

## Why the remaining work is slow

- Slow scenarios include 257 workspace semantic counterparts. `WorkspaceFixture` writes fresh projects and runs a restore and analysis worker for each revision, processing before and after sequentially. A typical case starts two restores and two workers. Four concurrent scenario classes limit overlap; cases within one class still form a serial tail.
- Both Workspaces and RealWorldCases explicitly disable parallelism for the entire assembly. The flags date to the initial suite implementations; the inspected history does not document a measured requirement for keeping every current test serial.
- Workspace `GitFixture` instances already use separate repositories and workspace caches, and configure environment variables on child processes. The inspected suites do not mutate the parent process environment. Establish the remaining isolation requirements instead of assuming an assembly-wide ban is necessary.
- Real-world cases share repository directories by `CacheName`. `RealWorldCaseStore.PrepareAsync` can clone or fetch into those directories without a preparation lock. Concurrent cold preparation of the same repository can race. Workspace analysis has its own per-cache-entry file lock; that does not protect repository preparation.
- Simply deleting the assembly flags will not distribute theory rows within `RealWorldCaseTests`, or cases within any other single class, across workers. Scheduling and class/collection boundaries must also be addressed.
- Workspace format assertions launch separate CLI commands for text, Markdown, and JSON, reanalyzing both revisions each time. `--no-restore` skips restoration, not analysis. Real-world format assertions similarly rerun the full command pipeline in process for each format and view.
- Child MSBuild workers and Roslyn analysis already perform work concurrently. Unbounded test parallelism can increase memory pressure and contention. Resource constraints must be measured, not used as an untested justification for serial execution.

## Performance targets

These are targets for this phase, not achieved measurements or permission to omit coverage.

| Scope | Target |
|---|---|
| Complete fast tier | At most 10 seconds on a prepared development checkout, including invocation startup; preserve current behavior coverage. |
| Relevant local integration selection | Normally at most two minutes, with explicit filters and failure on empty selections. |
| Slow scenarios in CI | At most six minutes per OS for coverage equivalent to the current 305 executions. |
| Workspace integration in CI | At most two minutes per OS for coverage equivalent to the current 26 executions. |
| Routine real-world/process cases in CI | At most three minutes per OS with prepared repository caches; report cold preparation separately. |
| Complete broad CI feedback | At most ten minutes per OS from job start through all required results, including setup and repository preparation; measure both cold and warm caches. If split into jobs, count the whole dependency path and report scheduling delay separately. |
| Total work | Reduce aggregate test-command time across workers against the baseline, and report runner-minutes and resource peaks. More workers alone must not masquerade as less work. |
| Explicit all-cases E2E | Establish a separate cold/warm baseline and demonstrate a matched improvement without reducing the pinned corpus. Do not substitute the routine subset. |

Record machine or runner type, OS, SDK, revision, selected cases, build state, cache state, concurrency, and complete invocation time. Keep setup and test execution distinguishable. Report targets that remain unmet; do not silently relax them to close the goal.

## Implementation priorities

### 1. Measure the expensive paths and establish isolation

- Use existing logs and bounded profiling samples before another broad run. Attribute time to repository preparation, Git calls, restore, worker startup, project loading, compilation, graph analysis, rendering, and snapshot verification where practical.
- Inventory mutable state and cleanup ownership: repository caches, workspace roots, restore outputs, process environment, snapshot destinations, and worker lifetimes.
- Identify which operations need exclusive access, which tests need exclusive execution, and which independent cases can overlap. Document a concrete reason for every remaining serialization boundary.
- Establish cold and warm baselines for representative slow scenarios, workspace cases, and real-world repositories. Measure process counts, peak memory, CPU use, and actual test overlap. Do not run overlapping benchmark suites on one host.

### 2. Enable useful bounded parallelism

- Replace blanket serialization with bounded execution of independent workspace and real-world cases after confirming isolation. Evaluate small concurrency levels, including one, two, and four, against the same cases on representative CI resources.
- Organize existing classes, collections, or deterministic shards so expensive theory rows can actually overlap. Merely removing `DisableTestParallelization` is not completion.
- Prepare shared repositories once per run or coordinate preparation per repository, including cold clones and missing-object fetches. Preserve pinned revisions and license checks. Avoid process-wide environment changes as a way to configure concurrent cases.
- Keep the environment-mutating scenario collection exclusive until its shared state is removed. Preserve timeout, cancellation, child-process cleanup, and fresh-process determinism checks.
- Account for nested Roslyn/MSBuild concurrency, memory, and long-running final collections. Choose the limit from measured throughput and stability, not available core count alone.

### 3. Eliminate repeated analysis and setup

- Reuse one analyzed revision pair or diff result across assertions that only vary the renderer. Preserve existing independent text, Markdown, and JSON expectations.
- Retain representative real CLI format routing, exit-code, diagnostic, command wiring, and fresh-process checks. Maintain real MSBuild coverage for project identity, references, frameworks, generators, configuration, and source changes.
- Reuse safe immutable prepared inputs and restored fixtures when restoration is not the behavior under test. Avoid restoring the same project shape for every semantic variant where isolation and correctness permit reuse.
- Scope any graph reuse to correct source, project, reference, framework, configuration, and generator inputs. Keep actual invalidation coverage; do not introduce stale results to meet a timing budget.
- Measure startup versus useful analysis before considering persistent workers. Prefer harness changes; do not change production behavior merely to conceal test overhead.

### 4. Shorten the CI dependency path

- Evaluate independent suite jobs or balanced shards after reducing duplicated work. Preserve all three supported operating systems and the complete current routine selection.
- Include repeated setup, cache downloads, additional runner-minutes, and memory in comparisons. Distinguish lower latency from lower total work.
- Verify cache hits and misses explicitly. A warm repository cache does not eliminate repeated compilation or graph analysis.
- Preserve actionable per-case failures, reproduction commands, timings, and existing result channels. Keep fast and representative feedback independent of broad jobs.

### 5. Validate equivalent coverage and measured improvement

- Maintain the behavior-to-layer inventory as existing tests are reorganized. Test counts may change only with an explicit mapping of retained assertions and integration boundaries; counts alone are not proof of equivalent coverage.
- Preserve reviewed snapshots and independently defined expectations. Never bulk-accept output changes to make a performance refactor pass.
- Run focused existing tests for each harness change, then use supported-platform CI for full confirmation. Use bounded repeat runs where necessary to check concurrency stability and timing variance.
- Report cold and warm results, median and range for repeated samples, resource peaks, aggregate work, and remaining serial tails. Separate routine CI from all-cases E2E results.

## Execution rules

- Work directly on `master`, using signed conventional commits and regular checkpoint pushes after appropriate focused validation. Keep EditorConfig, formatting, and commit-message gates; pushes must not wait for builds or tests.
- Do not add new tests unless explicitly requested. Reorganize or fix existing tests as needed; temporary diagnostic tests must be removed before committing.
- Keep production semantics, reviewed snapshots, pinned repositories, and required platform coverage intact. Moving coverage to scheduled/manual execution or deleting expensive permutations alone does not meet the target.
- Keep local profiling output in ignored artifacts and commit concise findings. Do not expand external artifact uploads or create bulk diagnostic bundles.
- Do not repeat passing checks without a relevant change, unresolved concern, or stated performance experiment. Continue independent work while broad CI runs; report pending and failed results accurately.
- Preserve the established mise fast/focused/E2E commands. This phase does not add a test hook or resume unrelated implementation work.

## Completion criteria

- [ ] The expensive paths have measured costs, and every remaining serial boundary has a documented isolation or resource reason.
- [ ] Independent workspace and real-world cases demonstrably overlap with bounded concurrency; shared repository preparation and cleanup remain safe under cold and warm execution.
- [ ] Format-only assertions reuse analysis, and redundant restoration/worker setup is reduced without losing meaningful integration checks.
- [ ] All per-suite and broad CI targets are met on Ubuntu, Windows, and macOS, with cold and warm conditions reported honestly.
- [ ] Aggregate work is reduced; any additional runner cost is quantified separately from elapsed-time improvement.
- [ ] The explicit all-cases E2E command has its own matched before/after evidence with the pinned corpus preserved.
- [ ] The current fast-tier budget and focused integration workflow remain intact.
- [ ] Equivalent behavioral coverage, unchanged reviewed expectations, concurrency stability, and passing supported-platform CI are documented in `docs/development-performance.md`.

Do not mark this phase complete solely because the fast tier passes, jobs have been split, or slow tests have moved out of the developer's way.
