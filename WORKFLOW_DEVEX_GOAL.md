# Development workflow and test-performance goal

Status: in progress. This document defines a separate development-experience workstream; it does not resume the paused implementation goal in `GOAL.md`.

## Outcome

Make routine development fast enough that people and agents naturally validate their changes. Most behavioral coverage should come from extremely fast tests near the code they exercise. Keep a smaller integration layer and a representative end-to-end layer. Long-running end-to-end tests are acceptable in CI and must not block local commits, pushes, or continued development.

Preserve confidence and meaningful coverage while removing repeated setup, process startup, restores, and analysis. Moving a slow suite to CI alone does not achieve this goal.

Progress, the behavior-to-layer inventory, measured samples and remaining evidence gaps are recorded in [development-performance.md](docs/development-performance.md). Completion criteria below require current evidence for their full scope.

## Starting evidence

- The former push hook spent 4.58 seconds building and 98 minutes running 639 scenarios. The build/test push gate has been removed.
- An earlier recorded 639-test run took 97.32 minutes with peak test overlap of one. Its 265 restored-workspace cases consumed 73.26 minutes.
- Scenario fixtures create temporary Git repositories and isolated workspace caches. Each CLI call starts a new process. Command, format, and mode combinations repeat compilation and workspace analysis; some also repeat restore.
- Signed checkpoint `5ff3053` enables four concurrent scenario classes and isolates environment-mutating partial-clone tests. The same 12-test sample improved from 70.316 to 44.805 seconds of command wall time. All 12 passed; all 10 partial-clone tests also passed. This is one Windows comparison, not a measured full-suite speedup.
- Current commit hooks check EditorConfig, .NET formatting, and conventional messages. Pushes have no build/test gate. Focused mise tasks exist for scenarios, workspaces, and real-world cases.

## Feedback layers and budgets

These are initial targets to validate, not achieved measurements. Record the machine, SDK, selected tests, cache conditions, test count, and revision with every comparison.

| Layer | Coverage and execution | Initial budget and trigger |
|---|---|---|
| Fast unit and semantic tests | In-process algorithms, binding/equivalence, graph construction, selection, traversal, alignment, diagnostics, and rendering. Use real minimal Roslyn inputs where semantics matter. | Complete fast tier at most 10 seconds on a prepared development checkout. Aim for ordinary cases below 100 ms. Run routinely and make it eligible for a local hook. |
| Focused component and integration tests | Actual Git behavior, CLI parsing and exit codes, project loading, generators, cache invalidation, cancellation, and process boundaries. | A relevant local selection should normally finish within two minutes. Run for changes in that area. |
| Representative CI integration | A bounded selection covering supported OSes, analysis modes, packaging, and important cross-component paths. | Aim for feedback within ten minutes. Run independently of long suites. |
| Broad end-to-end and real-world validation | Full pinned repositories, expensive workspace combinations, installed-package checks, recent-history sweeps, and cross-OS determinism. | May take much longer. Run asynchronously in CI or through an explicit diagnostic command. Never attach it to a local hook or make agents wait for it before pushing. |
| Benchmarks | Comparable measurements of the affected performance area. | Select relevant benchmarks explicitly; no automatic full benchmark matrix on commit or push. |

Measure complete invocation time, including runner startup and discovery. Report incremental build time and first-time restore separately, and also measure the actual total hook latency. A hidden build or restore must not turn a ten-second test claim into a multi-minute hook.

Aim for at least 80% of meaningful behavioral cases to live in the fast layer. Use that as a design signal, not a reason to inflate test counts or delete necessary integration coverage.

## Implementation sequence

### 1. Inventory cost and coverage

- Use existing timing records and bounded samples first. Rank test classes by wall time and identify repeated Git setup, CLI launches, restores, workspace loads, compilation, analysis, and rendering.
- Map each expensive test to the behavior it protects, the cheapest faithful layer, and the integration boundary that still needs real execution.
- Identify process-wide environment mutations and other shared state before increasing concurrency. Keep those cases exclusive until the shared state is removed.
- Record cold versus warm conditions explicitly. Do not launch overlapping broad suites while measuring throughput.

### 2. Build the broad, fast base of the pyramid

- Move pure semantic and graph assertions to direct library calls with small in-memory source inputs. Avoid a temporary Git repository and a new CLI process when neither is part of the behavior under test.
- Exercise real parsing, binding, and dispatch logic. Do not replace those systems with mocks that merely reproduce expected answers.
- Reuse immutable framework metadata and safe fixture inputs. Keep mutable state local to each test.
- Preserve independently defined expectations and negative controls. Match old and new behavior coverage before retiring redundant process-level permutations.
- Keep representative real CLI cases for argument handling, exit codes, stdout/stderr, process isolation, and source/MSBuild wiring. Keep real integration tests for every behavior that depends on those boundaries.

### 3. Stop recomputing analysis for renderer coverage

- Analyze a revision pair once in tests concerned with rendering; pass the result to the text, Markdown, and JSON renderers.
- Verify content, identities, ordering, locations, diagnostics, truncation, and formatting against independent expectations.
- Retain a small CLI format-selection matrix and fresh-process determinism checks. Do not multiply every semantic case by every command, renderer, and execution mode without a specific regression risk.
- Never bulk-accept changed snapshots or derive expected results from the implementation under test to make this migration pass.

### 4. Reduce component-test setup costs

- Restore unchanged immutable workspace fixtures once and reuse their restored inputs for subsequent commands when restore itself is not under test.
- Reuse immutable analyzed graphs where the assertion permits it. Do not cache across source, project, framework, configuration, reference, or generator-input changes without a correct invalidation key.
- Preserve dedicated restore, cache-invalidation, failure, cancellation, and isolation tests.
- Evaluate persistent workers or pooled fixture resources only with evidence that initialization cost dominates and isolation remains correct. Do not change production behavior solely to hide test overhead.

### 5. Tune bounded concurrency

- Start from the current four-class limit. Measure CPU, memory, process count, and wall time before changing it.
- Retain exclusive execution for tests that mutate process-wide state. Validate overlap boundaries from existing runner timings where possible.
- Account for child MSBuild and compiler parallelism. More concurrent test cases can make throughput worse through oversubscription or cache contention.
- Check the slowest remaining class or collection; a long serial tail limits total speedup even when other classes finish early.

### 6. Make the fast path obvious in mise and hooks

- Provide clearly named fast, focused-integration, and full-E2E commands. Proposed names are `test:fast`, `test:integration`, and `test:e2e`; they are not implemented merely because this document lists them.
- Make the documented everyday command select the fast tier. Keep slow commands explicit and retain useful filters. Fail clearly on invalid or empty selections rather than reporting a misleading successful check.
- Once the fast tier meets its budget, it may join a commit or push hook. Prefer one event, not duplicate execution on both. Target total warm local-hook latency below 15 seconds at p95, including formatting and any incremental build.
- Establish the hook budget with a small repeatable sample, including changed-code and unchanged-code cases. Measure cold startup separately. Hooks must not fetch upstream repositories, restore unrelated projects, run benchmarks, or silently fall back to the full suite.
- Until that budget is demonstrated, keep the present lightweight hooks. Do not reintroduce the existing scenario suite as a gate.
- Update `AGENTS.md`, `CONTRIBUTING.md`, `hk.pkl`, and `mise.toml` together when the policy changes. This goal authorizes a future genuinely fast test gate; it does not authorize a slow gate disguised as a fast task.

### 7. Keep slow CI useful without blocking iteration

- Separate fast feedback and long end-to-end execution so an expensive job cannot delay the fast result.
- Choose appropriate push, scheduled, and manual coverage. Avoid needlessly repeating identical full runs; consider superseded-run cancellation with accurate cancelled status.
- Make failures actionable with the failing case, revision, reproduction command, and available timings. Use existing result channels; do not expand external artifact uploads without the required authorization.
- Continue development while long jobs run. Investigate failures promptly and retain appropriate final release validation. Asynchronous feedback is not permission to ignore regressions or report pending tests as passed.

## Execution rules

- Implement and push small, reviewable improvements after relevant focused checks. Do not wait for another 90-minute local suite to validate each harness edit.
- Preserve test discovery, assertions, snapshot review, platform coverage, and real integration boundaries. Report exclusions and migration decisions explicitly.
- Repeat passing checks only for a relevant change, an unresolved concern, or a bounded performance experiment with a stated purpose.
- Keep raw local profiling output in ignored artifacts. Commit concise findings and useful source changes; do not create handoff ZIPs or bulk diagnostic bundles.
- Use mise-managed tools, signed conventional commits, and the existing master workflow. Do not start unrelated implementation work as part of this goal.

## Completion criteria

- [x] Existing slow tests have a behavior-to-layer inventory, and the expensive cases have an evidence-based migration plan.
- [x] Most meaningful behavioral cases run in the fast tier without per-case CLI, Git, restore, or MSBuild orchestration.
- [x] The complete fast tier meets the ten-second prepared-checkout target; startup, incremental build, and cold restore costs are reported honestly.
- [x] Renderer checks reuse analysis, while representative CLI format routing and determinism remain covered.
- [x] Unchanged component fixtures avoid redundant restoration and analysis with tested isolation and invalidation boundaries.
- [x] Concurrency is bounded, shared-state tests remain isolated, and before/after comparisons retain the same behavioral coverage.
- [x] Everyday commands and agent instructions clearly select the fast path; slow suites remain explicit.
- [x] Any added test hook meets the measured total latency budget and never launches the broad end-to-end suite. No test hook has been added.
- [ ] Fast CI results arrive independently of long suites; full E2E failures remain visible and actionable without blocking local iteration.
- [ ] Representative before/after measurements and supported-platform CI evidence substantiate improvements. No subset result is presented as a full-suite speedup.
