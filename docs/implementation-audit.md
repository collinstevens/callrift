# Implementation acceptance audit

The controlling brief is the September 24, 2026 continuation request. DESIGN.md describes the original architecture and acceptance criteria. Milestone commits are checkpoints. This checklist stays open until current evidence proves every requirement below.

Status: engineering work remains. This continuation starts at `2a7ae0756094663793a66a712c24221f23dacb70`. Work proceeds directly on `master`, with regular signed pushes to `collinstevens/callrift`. The implementation targets .NET 11 with SDK `11.0.100-rc.1.26425.128`. Git identity is Collin Stevens, with SSH signing enabled and the configured user key. No signature verification was performed.

Baseline evidence: [CI run 36073972663](https://github.com/collinstevens/callrift/actions/runs/36073972663) completed successfully at that exact commit. The baseline contains 51 scenario, ten workspace, and 11 real-world case tests. The manifest still contains ten distinct pairs across two repositories; only one pair has a restored snapshot. Passing baseline checks do not close the remaining requirements.

| Requirement | Current evidence | Remaining acceptance evidence |
|---|---|---|
| Lowercase branding; stable C# API names | README, package projects, baseline branding commit | Inspect final packages, help, docs, workflow artifacts |
| mise and hk; conventional signed commits | mise.toml, mise.lock, hk.pkl; effective Git signing configuration inspected | Run all hooks and inspect final clean history |
| CLI diff/tree/reach in both modes | Scenario, query, and workspace suites exist | Extend matrix to every comparison form, selection, locations, cancellation, deterministic output, text/Markdown/versioned JSON |
| Working tree, index, revision pairs, merge base | Scenario/query snapshots | Verify all forms in MSBuild as well as source mode |
| Identity and collection | Existing overload, generic, extension, partial, local, constructor, callback, method-group, conditional-access, branch scenarios | Audit semantic edge cases against real changes and independent CLI expectations |
| Dispatch, decorators, recursion, project boundaries | Existing dispatch, decorator, abstract, workspace-project snapshots | Audit selected interface roots, changed dispatch without syntax edits, multiple implementations, possible cycles, generated collisions |
| Package receivers, defines, frameworks, test exclusion | Workspace package/define tests; test classification scenarios | Review multi-target and large-project environments; preserve application roots |
| Incomplete analysis and reachability | Coverage fields and unresolved-call diagnostics exist | Audit compilation failures, omissions, strict exits, limits and cancellation; fix silent loss |
| Virtual overrides, accessors, indexers, operators, events, conversions, static initialization | DESIGN.md explicitly defers several families; README and MSBuild docs list gaps | Exercise each family, implement required behavior, and give precise reviewed evidence for any agreed limitation |
| Mediator/minimal API conventions | Top-level lambda scenario; source-only CleanArchitecture snapshots | Review restored convention-dependent cases; never imply runtime dispatch proof |
| Output parity and schema compatibility | schemas/output-v1.schema.json, docs/json.md, JSON snapshots | Validate every new snapshot, diagnostics, locations, omissions and truncation across formats |
| At least 60 reviewed distinct pairs, seven repositories, five per repository | manifest has ten pairs in Serilog/CleanArchitecture | Curate at least 50 more pairs in at least five additional repositories |
| Diverse real changes and large/mixed-language repositories | Existing real-world cases cover a small selection of changes | Include DI/CQRS/routing, generics/extensions, two large application/framework repositories, mixed language, substantial multi-project commits and all requested feature tags |
| License and immutable provenance | Current manifest has URLs, full SHAs, license names/paths; M1 review records license inspection | Record immutable license-file evidence, expected effects, mode/project/framework/configuration/selectors and specific known-wrong issue links |
| External no-checkout caches and object reading | RealWorldCaseStore and GitRepository implementations; all case-cache workflow paths now use a sibling of github.workspace and keys hash real-world-cases/manifest.json | Verify new inputs and actual final CI cache paths |
| Text/Markdown and JSON for every pair | Twenty source snapshots and one combined restored snapshot | Review every new format against real diff and surrounding code; no bulk acceptance |
| At least 20 dual-mode pairs in four repositories, including a large multi-project repository | One restored Serilog pair | Provision compatible SDKs via mise; record unsupported environments without fallback |
| Automatic and focused root views | Most current entries use focused selectors | Add automatic snapshots and compare roots with expected source relationships |
| Review evidence and specific issues | real-world-cases/reviews/m1.md, m2.md, m3.md | Per-case expectations, inspected source, all-format checks, locations, diagnostics, cycles and order; fix high-impact errors |
| Repeatability and three-OS output | Deterministic ordering code and workflow matrix exist | Repeated local runs plus actual Ubuntu/Windows/macOS CI runs with received-output inspection |
| Small/medium/very-large BenchmarkDotNet MemoryDiagnoser workloads | Six baseline exports; only small Serilog workload | Add pinned medium/large workloads and full environment exports |
| Isolated source stage floors | FloorBenchmarks includes Git/parse/reference/binding/map/equivalence/expand/align/render | Inspect isolation and compare floors with complete costs |
| Materialization/restore/workspace/generator floors | WorkspaceBenchmarks aggregates several stages | Separate stage measurements and account for worker allocations |
| Warm/fresh diff/tree/reach, both modes; cache conditions | Small source command and aggregate restored baselines | Complete workload/mode/process matrix; distinguish package and filesystem caches |
| Every optimization measured before/after | No new optimization made during this audit | Same workload/environment exports and unchanged reviewed correctness evidence |
| Comparable approximately 5,000-file prototype workload | Explicitly unmeasured in benchmarks/README.md | Execute comparable revision pair and investigate overhead |
| Meaningful noisy-runner regression reporting | Existing warning thresholds avoid declaring speedups | Verify comparisons and retain full exports; avoid incomparable machine claims |
| Build/scenario/real-world-cases/workspace/package/format/workflow checks | Baseline CI run 36073972663 passes all tasks on Ubuntu, Windows, and macOS | Record final passing output for all tasks at the final implementation commit |
| Bounded PR and all real-world cases in scheduled/manual runs | Current CI runs all ten real-world cases | Split representative and full execution, include manifest-keyed external caches |
| Separate recent-history crash sweep | CrashSweep returns nonzero on caught exceptions; scheduled artifact upload exists | Exercise many commits/repository; validate process timeout/cancellation and fatal crash reporting |
| Installed tool, dnx, separate consumer, packaged worker | Package-Smoke.ps1 exists | Execute and inspect all packaged paths and metadata |
| License, dependency notices, contributor agreement | LICENSE, third-party notices, docs/dependencies.md, CONTRIBUTING.md | Reaudit any dependency changes; preserve AGPL-3.0-or-later and required notices |
| README, benchmark docs, reviews, release checklist | Existing docs accurately admit several gaps, but remote authorization text is stale | Final evidence-linked docs; identify exact legal-holder/agreement inputs and CLA check state |
| Clean signed history, remote CI, release boundaries | Baseline master at 2a7ae07; baseline CI run 36073972663 passes | Signed checkpoints directly on master, final three-OS CI, clean tracked tree; NuGet publication remains unauthorized |

Every completion entry must cite a current file, command result, reviewed snapshot, benchmark export, package check, or CI run. A passing narrow test cannot close a broader row. External release blockers do not close unfinished engineering rows.

## Earlier audit checkpoint (historical evidence)

Fixed selected interface/abstract roots bypassing implementation expansion. Ten new observable CLI cases passed in both modes and all three formats, including single/multiple dispatch and contract signature matching. See `tests/Callrift.Scenarios/REVIEW.md`.

The first audit run passed 41 scenario tests, 11 real-world case tests, and nine of ten workspace tests. The workspace Projects case timed out at 60 seconds in the concurrent solution run, then passed in isolation in ten seconds. Later SDK/process fixes and the successful 2a7ae07 CI run supersede that failed run as baseline evidence. The original failure remains relevant to timeout coverage.

Five additional candidate repository caches and immutable license evidence are recorded in `real-world-cases/reviews/candidate-investigation.md`. No additional pair has been accepted yet. The full acceptance checklist remains open.

## Nested-expression checkpoint

`CallCollectionTests` reproduced three silent omissions in both analysis modes: switch pattern guards, delegate-producing invocation expressions, and explicitly cast callback lambdas. The collector now preserves those calls. Six real-CLI cases pass across diff/tree/reach and text/Markdown/JSON. Three added feature snapshots were read against their source and validated against JSON schema v1; the existing 28 feature snapshots remain unchanged. Detailed source expectations and span checks are in `tests/Callrift.Scenarios/REVIEW.md`.

Temporary fixture commits are unsigned locally and in CI, as explicitly directed by the owner. The CI setup comment prohibits adding fixture signing or signing-key setup. Case caches in CI, sweep, and benchmark workflows use an external sibling directory; manifest hashes remain in the keys. The final implementation still requires the three-platform CI gate.

Local checkpoint verification on September 24, 2026:

- Locked restore and the full solution test run passed: 60 scenario, ten workspace, and 11 real-world case tests (81 total). The scenario run finished in five minutes and 19 seconds. Results are in the respective projects' `TestResults` directories.
- `hk check --all`, `actionlint`, and `git diff --check` passed.
- `scripts/Package-Smoke.ps1` passed installed CLI analysis in both modes, the packaged workspace worker, a separate library consumer, and local-feed `dnx` execution. No package was published.
- The renamed `tools/Callrift.RealWorldCases` preparation command resolved all ten pinned cases from the external cache. Benchmark discovery succeeded with the renamed project reference.
- All 21 moved real-world snapshots and the manifest retain their original Git blob hashes. The rename did not change accepted output or pinned revisions.

The real-world case thresholds and performance matrix remain incomplete. These targeted correctness checks do not close those rows or the broader correctness audit.
