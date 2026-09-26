# callrift implementation goal

Status: **paused at the user's request**. Do not resume implementation until asked.

## Checkpoint and handoff policy

The implementation is at signed commit `c9bc7116ec1c960b9b3e371d959d3598e36575c1`. Fast-iteration policy is pushed in `55735e5e84fc34612fafe279d71188d84d7bb217`. That policy keeps formatting/EditorConfig and conventional-message commit checks, removes the pre-push build/test gate, and adds focused mise tasks. Its configuration/format checks passed; the focused task ran exactly one selected test and passed. All three focused tasks reject missing filters. Full CI results remain asynchronous.

The user rejected the generated handoff bundle. Deleted `docs/handoff/2026-09-26/` and its ZIPs in signed cleanup commit `2962aed8496de4090bb136a062e0bd2090351f0d`; the cleanup is pushed to origin/master. Keep restart notes in this tracked `GOAL.md`. Do not create or commit handoff ZIPs, bulk logs, snapshot bundles, or replacement handoff packages. Preserve meaningful source changes through normal reviewed commits when implementation resumes. This cleanup removes the bundle from the current tree; it does not rewrite published Git history.

The paths below refer to original local drafts/evidence under ignored `artifacts/`; they are not accepted implementation or snapshots. Do not assume those files exist on a different machine. Recreate missing evidence from immutable pins and the recorded expectations. Keep upstream caches outside the repository, adapt machine-specific paths, and rebuild matching parent/worker assemblies. Do not resume old process IDs or tool sessions.

## Iteration policy

- Commit gates are EditorConfig, .NET formatting, and conventional commit messages. Pushes do not run builds or tests.
- Run focused tests, builds, and benchmarks locally when relevant to the changed area. Use `mise run test:focused`, `workspaces:focused`, or `cases:focused` with a specific filter. Use benchmark filters for the affected performance area.
- Use `mise run check:changed` for unstaged formatting; commit hooks check staged files. The full `mise run check` remains available for CI and explicit full checks.
- CI provides delayed feedback from the full end-to-end suite. Push after appropriate focused validation and continue independent work. Do not stall checkpoints on full-suite completion.
- Do not repeat passing checks without a relevant change or unresolved failure. Report pending and failed checks accurately. Final release/completion evidence remains required.
- This policy supersedes all historical full-suite push requirements in archived evidence.

## Working rules

- Read `AGENTS.md`, `CONTRIBUTING.md`, and relevant design/review documents. Work directly on `master`; no branches or PRs.
- Inspect effective Git identity/signing before every project commit or rewrite. Sign using the configured key; use conventional commits. Never disable signing or verify signatures. Temporary fixture commits may be unsigned.
- Meaningful tests are authorized by this goal. No code comments or subagents.
- Every dotnet command must run through mise. SDK: `11.0.100-rc.1.26425.128`, target net11.0. Inspect logs/TRX because Windows wrappers can obscure child exit codes.
- Build matching parent CLI and worker assemblies before validation; preserve historical evidence and record new hashes. Never promote unread snapshots or claim a partial repeat is a passing full suite.
- Keep upstream source/materializations outside this checkout. Adapt original Windows paths on the new machine.
- The user explicitly requested both `GOAL.md` and `WORKFLOW_DEVEX_GOAL.md` be committed and pushed. Keep both tracked and current; do not replace them with handoff archives.
- Existing automatic-review rejections for CI failure-detail reads remain in force; do not bypass them. Prior permitted run listings are distinct.
- NuGet publication and unrelated account changes are not authorized. Legal/CLA owner inputs remain documented and unenforced.

## Push-delay retrospective

The old push gate built in 4.58 seconds, then ran 639 scenarios for 1 h 38 m. An earlier fully recorded 639-test run (`artifacts/static-coalesce-complete-scenarios-terminal.json`) took 97.32 minutes; summed test durations were 97.31 minutes and peak test overlap was one. `ScenarioTests.cs` explicitly disables test parallelization. Its 265 workspace=true cases consumed 73.26 minutes; 264 workspace=false cases consumed 18.32 minutes; other tests consumed 5.73 minutes.

`GitFixture` creates fresh Git repositories and isolated workspace caches and launches a new dotnet CLI process for each command. Tests multiply semantic cases across modes, commands, and formats. The workspace provider starts restore unless explicitly told no-restore, then starts a separate analysis worker. MSBuild node/server reuse is disabled. This explains expensive serial orchestration; there is no CPU/I/O profile establishing a precise allocation of time among startup, restore, compiler work, or filesystem operations.

Agent mistakes: made the full suite a push prerequisite, ran overlapping broad validation, and queued another full suite for a handoff-only commit despite an immediately preceding pass on unchanged production code. The corrected push policy prevents recurrence. The cleanup commit plus push completed in approximately 2.4 seconds. Do not restart a full suite to investigate this history. Future harness work should measure stages, remove repeated analysis/restores for formatting assertions, separate broad semantic checks from representative process-level checks, and assess bounded parallelism after checking shared state. This is follow-up guidance, not permission to resume the paused goal.

## Test-runtime follow-up completed

At the user's request, signed commit `5ff3053f0edd80bceb1354e3a211cc1ea328c71c` enables up to four concurrent scenario test classes and moves PartialCloneTests into an exclusive ProcessEnvironmentCollection. The commit is pushed. The same 12 existing tests passed before and after: 70.316 s to 44.805 s command wall time (reported test duration 69 s to 43 s). TRX confirms peak test overlap increased from one to four and the exclusive test overlapped no other tests. All 10 PartialCloneTests then passed. Build and formatting checks passed. This is a bounded Windows comparison, not a measured full-suite runtime or cross-OS pass. Evidence remains local under `artifacts/test-throughput-{baseline,candidate}*`; do not bundle it into Git.

Remaining large test-runtime opportunities: move pure semantic checks to the library layer while retaining representative process-level CLI/MSBuild coverage; reuse analyzed graphs for format assertions; avoid repeated restores of unchanged immutable fixtures; measure per-class costs and tune bounded concurrency. Preserve independent expectations and real integration coverage. Do not launch a full local suite as a checkpoint gate.

Automatic approval review rejected changing CI artifact retention from failure-only to always because it expands test-result and snapshot uploads. No CI workflow change was made. Do not retry or bypass that rejection without authorization. The main implementation goal remains paused.

## Resume sequence

1. Read this goal, `AGENTS.md`, and `CONTRIBUTING.md`; inspect the current checkout and available local drafts. Keep the goal paused until explicitly resumed.
2. Resume source-grounded declaration review, then validate and integrate coherent changes with focused checks. Do not count the next 70 partially read ASP.NET declarations as reviewed.
3. Finish the pending OrchardCore and ASP.NET pair reviews without bulk-accepting snapshots.
4. Continue the remaining 29 pairs, semantic audit, performance matrix, packaging/release checks, and final three-OS CI evidence.

## Candidate implementation

| Change | Authoritative draft | Validation and remaining work |
|---|---|---|
| Declaration signatures | `artifacts/declaration-signatures-v5-core/Analysis/SymbolNames.cs` | Adds semantic access/modifiers, parameter names/defaults/optionality/extension/scoped markers, nullable/ref returns, and canonical method/containing-type constraints. Stable IDs and interceptor labels remain unchanged. 42 compiler-valid cases, four declaration regressions, 16 interceptor checks, 11 query repeats, nine workspace repeats, and 11 generic-context tests passed. Broad snapshot review remains incomplete. |
| Generator input order | `artifacts/stable-additional-inputs-worker/WorkspaceAnalysis.cs` | Preserves evaluated AdditionalFiles order with deterministic document IDs before compilation. Independent Z,A,M,B-to-C fixture failed on baseline and passed on candidate. Full 43-workspace suite passed. |
| Workspace lock contention | `artifacts/workspace-lock-worker/MSBuildAnalysisProvider.cs` | Waits with cancellation for recognized contention instead of failing after 600 retries. Other I/O errors propagate. The complete worker directory also contains the input-order correction. A real 90-second held-lock test failed on baseline and passed on candidate; cancellation and invalid-path tests passed. Linux/macOS runtime checks remain pending. |

Historical v5 Core SHA-256: `a91b27cadf3c077578a61e54b0078fed6c902b2323facf84029142167dccd2aa`.

Historical latest lock-worker SHA-256: `30019feb4b2b7837edef5a522994d9006dd0091005bd336857c56aed6cdae4da`.

The v5 broad suites used an earlier worker without the ordering and lock corrections. Do not attribute their results to the latest combined candidate.

Local test drafts include `DeclarationSignatureTests.cs`, `GenericContextBoundaryTests.cs`, `ConstraintDispatchTests.cs`, `AdditionalFileOrderTests.cs`, and `WorkspaceLockTests.cs` in their named artifact directories. The private `declaration-signatures-v5-accepted-scenarios/` tree also preserves the reviewed constructor assertion and scenario/query snapshots. `declaration-signatures-v5-workspaces/` preserves the nine reviewed workspace snapshots. Compare these with production before integration; private project files contain temporary build references.

Documentation drafts are in `declaration-signatures-docs/` and `stable-additional-inputs-docs/`.

## Validation at pause

- The broad v5 scenario run completed: **606 passed, 37 failed, 643 total**, approximately 1 h 52 m. The failures are 33 Verify differences and four ConstraintDispatch assertions. Reviewed private corrections separately passed 34/34 ScenarioTests and 4/4 ConstraintDispatch tests. No passing full candidate scenario run is claimed. Terminal evidence: `artifacts/declaration-signatures-v5-reviewed-scenarios-terminal.json`.
- The broad v5 accepted corpus completed: **4 passed, 91 failed, 95 total**. All 91 failures are Verify differences; no non-Verify failures. None of those received corpus snapshots has been promoted. Terminal evidence: `artifacts/declaration-signatures-v5-corpus-terminal.json`.
- The fresh latest-worker Orchard repeat completed all four JSON views with exact byte equality and parsed JSON equality. Terminal evidence: `artifacts/orchardcore-esmodule-stable-json-repeat-terminal.json`. All twelve earlier JSON/text/Markdown outputs completed; each text/Markdown pair matches. This repeat establishes reproducibility on this Windows machine, not cross-OS acceptance.
- Production checks passed **639/639 scenarios** for both `6c517180d3fc698df7f8c5a6e64575fa22062fe1` and `c9bc7116ec1c960b9b3e371d959d3598e36575c1`. The c9 push completed after a 1 h 38 m scenario run. The duplicate handoff-only run was stopped at the user's request. The pre-push build/scenario gate was removed; the full suite remains in three-OS CI. The stopped duplicate run is not a pass or a failure.

All isolated analysis jobs are terminal. Old process IDs and tool session IDs in historical evidence cannot be resumed on another machine. Do not restart superseded v2/v4 jobs.

## Exact review position

The final signature inventory contains **134 received files, 6,464 distinct signature changes, and 31 other differences**. The corpus ledger records **3,971** reviewed signature IDs; the scenario review covers another **86**. **2,407 ASP.NET declarations remain unreviewed.** All other repository declarations and all 31 Ocelot non-signature differences have source-grounded review records.

Resume from `artifacts/declaration-signatures-v5-corpus-declaration-review.json` and `artifacts/declaration-signatures-v5-snapshot-differences.json`. `read-v5-signature-batch.py aspnetcore 0 70` selects the current unreviewed set; offsets change after recording a batch. `read-v5-signature-supplements.py` reads containing constraints/static context. Read pinned declarations and supplements before adding IDs to the ledger. Helpers retain original external cache paths and need adaptation on the new machine.

The next 70 declaration headers were read but **not recorded as reviewed** before pause. They run from ID `773ffd73bfee68d2` (`BaseView.ExecuteAsync`) through `9f29df9f1351cb8b` (`EndpointHtmlRenderer.PrerenderedComponentHtmlContent.WriteTo`). Their supplemental constraints/static context review is unfinished. Do not increment the ledger from those partial reads.

## Pending OrchardCore pair

Pins: `b304fcd78a70b792c6f63916c0ce6e6957bb1aa0` to `4c1d10e68dd443c8bc9fc8d8a02059081fc6be12`. BSD-3 license blob: `183936c000fa2cc5969ba724afc1bdc5bfae5080`. Pair remains outside the accepted manifest.

`orchardcore-esmodule-source-expectations.json` records 31 C# diffs and pinned blobs. Latest outputs use `orchardcore-esmodule-localization-stable-additional-{source,msbuild}-{roots,focused}.{json,text,md}`. Source automatic: 389 roots, 6,017 nodes, 16,779 diagnostics. Restored automatic: 424 roots, 8,179 nodes, 19 diagnostics. Both focused views: six roots, 2,181 nodes. All views are truncated.

All rendered source/restored output was read. Source/diagnostic bounds were checked against 2,195 pinned blobs and 4,348 revision paths. All 12,954 generated locations were checked against independently regenerated 71 files per side. OpenAPI cache order now repeats; its sole content removal matches the removed XML documentation. Proof files use the `orchardcore-esmodule-stable-` prefix.

Root connectivity audits completed: source before/after roots 378/388 with 705 shared edges each; restored 413/423 with 810 shared edges each. Both have 48 changed seeds. Unchanged `Configure` correctly changes call binding because `BuildManifest` becomes static. Connectivity checks are internal graph consistency evidence. Independent review of all root edges, remaining JSON signatures, and the large source diagnostic set remains unfinished. All 19 restored unresolved calls were independently traced to dynamic receivers in nine unchanged pinned files. Keep conservative dispatch distinct from proven runtime behavior.

## Pending ASP.NET pair

Pins: `96b7ae9999a954147cabc0245063f87cf2a0029b` to `7638d7114ad4a74465ad10e6d61d0915067b80c3`. Pair remains unaccepted. Evidence prefix: `aspnetcore-upload-stream-ownership-`.

V5 source automatic outputs have 1,296 roots, 21,464 nodes, and 23,099 diagnostics. Expanded focused outputs have 12 roots and 402 nodes. All three formats completed and text/Markdown match. Focused rendering was read against the source expectations. Full JSON, locations, diagnostics, root witnesses, repeats, and restored-mode trial remain pending.

Known limitation: `TryComplete(string,long)` owner-mismatch and failed-removal guards only return false. `CallCollector.Branch` drops branches with zero calls, so these guards are absent. The graph shows `TryGetValue`, `KeyValuePair.Create`, `TryRemove`, and converter completion. Fix or document this precise limitation before acceptance. Do not claim the ownership guard is visible.

## Remaining goal scope

Accepted coverage is **31 pairs, 181 snapshots, 91 full views**, plus four sweep tests. Five pairs each: Serilog, CleanArchitecture, Autofac, Polly, Ocelot. OrchardCore has four; ASP.NET Core has two. The goal requires at least 60 pairs across seven repositories with at least five each: **29 pairs remain**. The both-mode threshold is met by 20 pairs across five repositories; final repeatability remains outstanding.

The semantic audit still includes accessors, indexers, operators, events, conversions, conventions, receiver guard/value flow, and compiler diagnostics. Performance still needs the full stage and complete-command matrix, both modes, warm/fresh processes, worker allocation accounting, and an actual comparable 5,000-file pair. Existing Git/depth-stage results are not full-command speedup claims.

Final installed package, dnx, library consumer, packaged worker, build, scenario, workspace, corpus, formatting, workflow, recent-revision sweep, three-OS CI, documentation, and release checks remain required. Legal/CLA owner inputs are documented but enforcement is not activated. Previously rejected CI failure-detail reads remain unauthorized; do not bypass automatic approval review. Prior permitted run listings are distinct.

Finish the signature review, validate the latest combined candidate, integrate coherent reviewed changes, and push signed checkpoints. Then finish the pending pairs and the remaining full objective. Do not treat this handoff as completion.

## Full objective and completion requirements

Finish callrift's implementation and validate it against complex real-world C# changes. Continue through correctness, independently reviewed snapshots, performance, packaging, and actual CI. Do not substitute documentation of unfinished work for completion.

1. **Correctness.** Exercise `diff`, `tree`, and `reach` through the real CLI in source-only and restored MSBuild modes. Cover working-tree, staged, revision-pair, and merge-base comparisons; entry/file selection; locations; cancellation; deterministic text, Markdown, and versioned JSON. Audit semantic identity, overloads, generics, extension/partial/local methods, constructors, callbacks, method groups, conditional access, branches, recursion, and project boundaries. Address multiple implementations, decorators, interface/abstract/virtual dispatch, false recursion, guards, moved subtrees, signatures, root matching, and dispatch changes without caller edits. Include generated code, duplicate types, package receivers, real defines, multiple target frameworks, application entry points despite test exclusion, unresolved calls, compilation failures, incomplete reachability, accessors/indexers/operators/events/conversions/static initialization, mediator conventions, and minimal API handlers. Possible dispatch must not imply proven runtime execution. Keep identities, locations, diagnostics, coverage, truncation, and rendered output consistent. Preserve schema compatibility or explicitly version breaking changes. Support genuine limitations with reviewed evidence.
2. **Real-world coverage.** Accept at least **60 distinct revision pairs across at least seven popular repositories, with at least five pairs per repository**, including two large applications/frameworks and one mixed-language repository. Retain useful cases; include substantial multi-project features as well as guards, exceptions, async callbacks, method groups, interface extraction, DI rewiring, overloads, cross-project calls, signatures, renames, moved subtrees, and generated code. Record URL, full immutable SHAs, pinned license evidence, rationale, feature tags, options, independent expectations, review evidence, and specific known-wrong issues. Every pair needs text/Markdown and JSON snapshots. At least **20 pairs across four repositories**, including a large multi-project repository, must exercise both modes with automatic affected-root and focused views. Selectors must not hide discovery/dispatch defects. Record unsupported environments without silent fallback. Keep upstream source and restored materializations outside this checkout; use external no-checkout Git caches and read Git objects directly. Key CI input caches by manifest content.
3. **Snapshot review.** Read pinned source diffs and surrounding code before acceptance. Check additions/removals/moves, guards, preserved calls, identities, dispatch, locations, diagnostics, omissions, cycles, and ordering across every format. Never bulk-accept unread output or normalize incorrect behavior to make tests pass. Fix high-impact trust problems and document remaining known-wrong cases. Prove repeatability across runs and supported operating systems. Exclude timings, absolute paths, machine-specific data, and unstable identifiers.
4. **Performance.** Use BenchmarkDotNet with MemoryDiagnoser on pinned small, medium, and very large repositories. Isolate Git listing/blob reads, parsing, references/compilation, binding, implementation mapping, equivalence, expansion, alignment, rendering, materialization, restore, workspace opening, and generators. Measure complete diff/tree/reach in both modes with warm and fresh processes; distinguish package/filesystem cache conditions and account for MSBuild-worker allocations. Compare optimizations on equivalent workloads/environments, retain correctness snapshots, and commit full exports with environment metadata. Reconcile stage and complete-command costs. Evaluate the prototype's approximately 30-second result on an actual comparable **5,000-file revision pair**. Do not claim speedups from incomparable machines or noisy measurements; keep CI regression reporting meaningful.
5. **Operations and release readiness.** Pass build, scenarios, real-world cases, workspaces, packaging, formatting, and workflow checks. Use bounded representative cases in routine CI and all pinned cases in scheduled/manual runs. Maintain a separate recent-revision sweep with timings, failures, timeouts, cancellation, and failure on crashes. Verify the **final pushed implementation on Ubuntu, Windows, and macOS**. Investigate received snapshots. Exercise installed packages, `dnx`, a separate library consumer, and the packaged MSBuild worker; verify metadata, SDKs, branding, licenses, and release behavior. Finalize usage/limitations, benchmark docs, case reviews, and release checklist. Identify owner-supplied legal/CLA decisions accurately; do not claim enforcement exists before activation.

Completion requires evidence for every requirement, working packages, passing final CI, and a clean tracked tree. Keep the goal incomplete while mandatory engineering work remains. Report external legal/release inputs separately. NuGet publication and unrelated account changes are not authorized.
