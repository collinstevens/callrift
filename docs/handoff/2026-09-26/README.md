# Paused implementation handoff

Work is paused at the user's request for continuation on another machine. The full goal is incomplete. Production code remains at the implementation in `c9bc7116ec1c960b9b3e371d959d3598e36575c1`; this checkpoint preserves drafts and evidence without promoting candidate code or snapshots.

Read [the full objective](objective.md), then this handoff. The archives preserve the current implementation drafts, test drafts, private snapshots, review ledgers, runners, and validation evidence. They exclude rebuildable binaries, packages, external upstream checkouts, restored workspaces, and obsolete scratch directories. Historical evidence retains original paths and hashes. Those paths describe the original Windows machine and must be adapted before rerunning scripts.

## Resume on another machine

1. Pull `origin/master`, read the repository `AGENTS.md`, and install the versions in `mise.toml` and `global.json`. Every dotnet command must run through `mise exec -c 'dotnet ...'`. The current SDK is `11.0.100-rc.1.26425.128`.
2. Verify archive SHA-256 values from `archives.json`. Extract every `drafts-and-evidence-*.zip` into the repository root. Entries restore to the ignored `artifacts/` directory. Allow approximately 3 GB of disk space. Each file has its original path, size, and SHA-256 in `manifest.json`.
3. Recreate a local `GOAL.md` using `objective.md` and this handoff. Add `/GOAL.md` only to `.git/info/exclude`. Keep that local file out of commits. Resume the goal explicitly in the new session.
4. Recreate upstream no-checkout caches outside the repository from the manifest URLs and immutable revision pins. Restore dependencies and generated-source audits on the new machine. Do not copy upstream source into this checkout.
5. Rebuild candidate assemblies from the preserved source. Frozen binaries are deliberately omitted. Private projects often reference old `*-ready` assemblies; update those references to newly built matching assemblies. Keep the parent CLI and its `msbuild/` worker overlay consistent. Record new hashes instead of claiming they equal historical Windows binaries.
6. Continue the review and integration sequence below. Archived received snapshots remain unaccepted. Preserve the distinction between a reviewed subset, a passing isolated repeat, and a passing full suite.

Work directly on `master`; no branches or PRs. Inspect effective identity and signing configuration before every commit, sign with the configured key, use conventional commits, and push through normal hooks. Never bypass hooks or verify commit signatures. Meaningful tests are explicitly authorized by the full goal. No subagents or code comments. NuGet publication and unrelated account changes remain unauthorized.

## Candidate implementation

| Change | Authoritative draft | Validation and remaining work |
|---|---|---|
| Declaration signatures | `artifacts/declaration-signatures-v5-core/Analysis/SymbolNames.cs` | Adds semantic access/modifiers, parameter names/defaults/optionality/extension/scoped markers, nullable/ref returns, and canonical method/containing-type constraints. Stable IDs and interceptor labels remain unchanged. 42 compiler-valid cases, four declaration regressions, 16 interceptor checks, 11 query repeats, nine workspace repeats, and 11 generic-context tests passed. Broad snapshot review remains incomplete. |
| Generator input order | `artifacts/stable-additional-inputs-worker/WorkspaceAnalysis.cs` | Preserves evaluated AdditionalFiles order with deterministic document IDs before compilation. Independent Z,A,M,B-to-C fixture failed on baseline and passed on candidate. Full 43-workspace suite passed. |
| Workspace lock contention | `artifacts/workspace-lock-worker/MSBuildAnalysisProvider.cs` | Waits with cancellation for recognized contention instead of failing after 600 retries. Other I/O errors propagate. The complete worker directory also contains the input-order correction. A real 90-second held-lock test failed on baseline and passed on candidate; cancellation and invalid-path tests passed. Linux/macOS runtime checks remain pending. |

Historical v5 Core SHA-256: `a91b27cadf3c077578a61e54b0078fed6c902b2323facf84029142167dccd2aa`.

Historical latest lock-worker SHA-256: `30019feb4b2b7837edef5a522994d9006dd0091005bd336857c56aed6cdae4da`.

The v5 broad suites used an earlier worker without the ordering and lock corrections. Do not attribute their results to the latest combined candidate.

Preserved test drafts include `DeclarationSignatureTests.cs`, `GenericContextBoundaryTests.cs`, `ConstraintDispatchTests.cs`, `AdditionalFileOrderTests.cs`, and `WorkspaceLockTests.cs` in their named artifact directories. The private `declaration-signatures-v5-accepted-scenarios/` tree also preserves the reviewed constructor assertion and scenario/query snapshots. `declaration-signatures-v5-workspaces/` preserves the nine reviewed workspace snapshots. Compare these with production before integration; private project files contain temporary build references.

Documentation drafts are in `declaration-signatures-docs/` and `stable-additional-inputs-docs/`.

## Validation at pause

- The broad v5 scenario run completed: **606 passed, 37 failed, 643 total**, approximately 1 h 52 m. The failures are 33 Verify differences and four ConstraintDispatch assertions. Reviewed private corrections separately passed 34/34 ScenarioTests and 4/4 ConstraintDispatch tests. No passing full candidate scenario run is claimed. Terminal evidence: `artifacts/declaration-signatures-v5-reviewed-scenarios-terminal.json`.
- The broad v5 accepted corpus completed: **4 passed, 91 failed, 95 total**. All 91 failures are Verify differences; no non-Verify failures. None of those received corpus snapshots has been promoted. Terminal evidence: `artifacts/declaration-signatures-v5-corpus-terminal.json`.
- The fresh latest-worker Orchard repeat completed all four JSON views with exact byte equality and parsed JSON equality. Terminal evidence: `artifacts/orchardcore-esmodule-stable-json-repeat-terminal.json`. All twelve earlier JSON/text/Markdown outputs completed; each text/Markdown pair matches. This repeat establishes reproducibility on this Windows machine, not cross-OS acceptance.
- Earlier normal production checks passed 639/639 scenarios for `6c517180d3fc698df7f8c5a6e64575fa22062fe1`. The already-running normal push for `c9bc711` was allowed to finish through its required hooks. The final checkpoint push also uses normal hooks; consult the push result rather than treating archived candidate failures as production-hook results.

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
