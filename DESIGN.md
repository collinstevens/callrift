# callrift

Design · 2026-09-24 · M1 and M2 implemented and verified

Produce reviewable C# call-flow diffs from Git snapshots. The primary acceptance example is the supplied orders flow: discover the controller, follow interface dispatch, retain validation and persistence, and show pricing nested beneath the new timeout wrapper.

**Names and platform**

Project, repository, tool package, and executable: `callrift`. The separately consumable library package is `callrift.core`; a tool package and a conventional library need distinct package identities. Public branding and package IDs use lowercase. C# namespaces and types retain their existing PascalCase names. The MSBuild adapter can be a separate `callrift.msbuild` package.

On September 24, public [GitHub repository search](https://api.github.com/search/repositories?q=callrift+in:name) returned zero results. NuGet's flat-container endpoints for [the tool](https://api.nuget.org/v3-flatcontainer/callrift/index.json) and [the proposed library](https://api.nuget.org/v3-flatcontainer/callrift.core/index.json) returned HTTP 404. No collision was found. These checks do not reserve names or expose private repositories or reserved NuGet prefixes; repeat them before publication.

Target `net11.0` per the owner's September 24 update. Pin SDK `11.0.100-rc.1.26425.128`, the [.NET 11 RC1 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/11.0), through mise and matching `global.json`. Keep SDK 10.0.303 available through mise for pinned historical projects used by real-world cases. Verify package compatibility when selecting exact dependency versions.

**Architecture: proposed calls**

These are proposed method names, not output from an implemented tool. Every added node belongs to the new project.

```diff
+ Program.Main
+ └─ CommandRunner.RunAsync
+    ├─ RevisionResolver.ResolveAsync
+    │  ├─ GitRepository.FindRootAsync
+    │  └─ GitRepository.ResolveSnapshotAsync
+    ├─ CallriftService.DiffAsync
+    │  ├─ SnapshotReader.ReadAsync
+    │  │  ├─ GitRepository.ListEntriesAsync
+    │  │  └─ GitBlobReader.ReadBatchAsync
+    │  ├─ IAnalysisProvider.AnalyzeAsync
+    │  │  ├─ SourceOnlyAnalysisProvider.CreateCompilation
+    │  │  └─ CallGraphBuilder.Build
+    │  │     ├─ MemberIndexer.Index
+    │  │     ├─ CallCollector.Collect
+    │  │     └─ DispatchResolver.FindTargets
+    │  ├─ ChangeDetector.Compare
+    │  ├─ EntrySelector.FindAffectedRoots
+    │  ├─ TreeExpander.Expand
+    │  └─ TreeDiffer.Compare
+    ├─ PresentationBuilder.TrimAndCollapse
+    └─ OutputWriter.WriteAsync
```

The library owns snapshots, graphs, queries, diffs, and rendering. The CLI owns argument parsing, terminal detection, diagnostics routing, and exit codes. Analysis providers produce the same graph contract. Roslyn symbols stay inside an analysis session; public results contain portable identities and locations. All expensive operations accept cancellation and use a shared bounded concurrency budget.

```text
IAnalysisProvider.AnalyzeAsync(SourceSnapshot, AnalysisOptions, CancellationToken)
  -> AnalysisResult(CallGraph, AnalysisCoverage, Diagnostics)

CallriftService.DiffAsync(DiffRequest, CancellationToken) -> DiffResult
CallriftService.TreeAsync(TreeRequest, CancellationToken) -> TreeResult
CallriftService.ReachAsync(ReachRequest, CancellationToken) -> ReachResult
```

`CallGraph` holds members and ordered call-site edges. Edge kinds distinguish direct calls, possible dispatch targets, and callbacks. Branches preserve lexical control structure. A callback nested below its receiving call means “passed here and may execute”; it does not establish that the callee invokes it synchronously or at all. This is static analysis, so `reach` reports potential paths.

**Snapshot and analysis boundaries**

Source-only mode performs no checkout, restore, project evaluation, or build. Resolve revisions to immutable object IDs before reading. Enumerate revision paths and blob IDs with NUL-delimited `git ls-tree`; read blobs through one long-lived `git cat-file --batch` process per snapshot. Write requests on a separate task with BOM-less UTF-8, consume exact byte lengths, and drain stderr. Blob-ID requests avoid newline-containing path hazards.

For the working tree, use `git ls-files -z --cached --others --exclude-standard`, deduplicate paths, omit deleted files, and include eligible untracked files. Capture contents once; detect concurrent file changes and report an inconsistent snapshot instead of silently mixing versions. For `--staged`, read stage-zero index entries and their blobs, never working-tree contents. Reject unmerged index entries. Exclude exact `bin` and `obj` path segments. Strip source BOMs, preserve Git path case, and normalize output separators to `/`. Report skipped submodules and unsupported inputs.

Create one source-only compilation per side with BCL references selected from `TRUSTED_PLATFORM_ASSEMBLIES`, a fixed C# language version, and synthetic standard SDK implicit global usings. Do not accidentally reference the tool's own dependency assemblies. Record these assumptions in coverage metadata. Multiple projects, conditional compilation, missing packages, and generators limit this mode. Detect duplicate declarations and unresolved bindings; do not resolve collisions by enumeration order. [Roslyn's semantic model](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis) supplies symbol binding within the selected compilation context.

Read lightweight project metadata without evaluating MSBuild to identify test projects. Prefer literal `IsTestProject` and known test-framework references, including straightforward local props imports. Treat conditional or computed metadata as uncertain. Use exact conventional directory segments only as a fallback; `Latest` must remain production code. Exclude test members from dispatch candidates, caller traversal, and roots by default; `--tests` includes them. Diagnose uncertain project membership.

M3 replaces loading while retaining graph and query behavior:

```diff
 IAnalysisProvider.AnalyzeAsync
   SourceOnlyAnalysisProvider.AnalyzeAsync
+  MSBuildAnalysisProvider.AnalyzeAsync
+  ├─ SnapshotMaterializer.MaterializeAsync
+  ├─ WorkspaceWorker.RunAsync
+  │  ├─ MSBuildLocator.RegisterInstance
+  │  ├─ RestoreRunner.RestoreAsync
+  │  ├─ MSBuildWorkspace.OpenSolutionAsync
+  │  └─ Project.GetCompilationAsync
+  └─ CallGraphBuilder.Build
```

Materialize complete revision/index/working snapshots into disposable directories, including projects, props, targets, additional files, and NuGet configuration. Restore and design-time evaluation happen there. Preserve the user's checkout. Report dependencies outside the captured root instead of silently substituting current files. Record SDK, configuration, TFM, and restore inputs. Require `--framework` when a project has several target frameworks; analyze the selected framework consistently on both sides.

Use separate workspace worker processes per side so different historical SDK requirements can coexist. [Register MSBuild Locator before loading MSBuild types](https://learn.microsoft.com/en-us/visualstudio/msbuild/find-and-use-msbuild-versions?view=vs-2022). Obtain generated documents from the project compilation and assign stable logical paths. Missing SDKs, restore failures, workspace failures, and generator failures remain explicit; never silently fall back to source-only mode. MSBuild mode evaluates repository build logic and runs generators, so it is intended for trusted inputs. Better binding still does not prove runtime DI registration or mediator routing.

**Collection and trustworthy identities**

Index block and expression-bodied methods, explicit and primary constructors, local functions, partial declarations, and per-file top-level entrypoints. Recognize source record implicit constructors as source leaves. Attach constructor initializers and executable instance initializers to their owning constructor flow without double counting chained constructors.

Walk receiver calls before invocation arguments, then emit the invocation. Ordinary argument calls precede it; inline lambda bodies and method groups become callback children. Keep callback bodies out of the enclosing member's sibling calls. Index local functions separately and expand them only when called. Cover generic invocations, extensions, `await`, and conditional access.

Returned and assigned anonymous functions and delegate-converted method groups retain their bodies under an explicit callback node. Creating or returning a delegate establishes a possible callback relationship; it does not prove invocation. Calls that evaluate a method group's receiver remain outside that deferred node.

Use `GetSymbolInfo`; normalize `(ReducedFrom ?? method).OriginalDefinition`. Candidate symbols are evidence of incomplete binding, not a successful overload decision: retain candidate identities with an uncertainty marker instead of selecting the first one. Delegate invocation remains external even for a source-declared delegate type. Unresolved calls remain visible as `? receiver.Method`; resolved external leaves are hidden unless `--externals`. External wrappers with visible callback descendants remain visible.

Human labels are concise, such as `Outer.Inner.Run`, `Repo<T>.Load`, `Outer.Local`, and `new Order`. Canonical identities include project/TFM scope when available, namespace, containing type, generic arity, parameter types/ref kinds, and local-function owner. Keep a separate signature representation for return types and other changes that do not alter overload identity. Never use a short display label as a unique graph key.

Map interfaces with `FindImplementationForInterfaceMember`; follow abstract override chains. One source candidate renders `IFoo.Bar → Foo.Bar`; multiple candidates render ordered `⇢ Impl.Bar` children. Both forms mean possible in-scope implementations. Represent decorator cycles as possible dispatch cycles, not proven recursion. Only a repeated canonical member on the active expansion path triggers a cycle marker; overloads and earlier appearances elsewhere do not.

Emit condition-bearing nodes for if/else, switch sections and expressions, try/catch/filter/finally, loops, conditional expressions, short-circuit expressions, and conditional access when they contain visible calls. Preserve calls in conditions, collection expressions, and loop headers in their proper context. Labels collapse whitespace and truncate for display; comparison uses complete normalized condition syntax so truncation cannot hide a change. Loops show structural order, not iteration counts. Guard clauses that only return or throw require a body-change marker in M1; do not invent a downstream predicate without control-flow analysis.

Virtual non-abstract dispatch, property/indexer bodies, operators, event dispatch, implicit conversions, reflection, and arbitrary delegate dataflow remain explicit coverage gaps for the initial release. Package conventions such as mediator handler routing need later evidence-based dispatch adapters. Accurate mode alone does not fill these gaps.

**Changes, roots, and presentation**

Seed changes from additions/removals, signatures, and body non-equivalence using `SyntaxFactory.AreEquivalent(..., topLevel: false)`. Also compare normalized bound edges and dispatch target sets: unchanged syntax can bind differently after edits to interfaces, imports, or overloads. A changed body with unchanged visible calls renders `~ Method (body changed; visible calls unchanged)` so a meaningful edit cannot disappear.

Traverse reverse edges in the union of both graphs, including implementation-to-interface links. Select affected members without callers; condense strongly connected components first so cycles cannot erase all roots. Choose stable representatives for cyclic roots. Explicit entry/file selection bypasses automatic root inference. This finds static roots, not guaranteed application entrypoints; framework callbacks may be independent roots.

Match exact identities first. Match remaining roots and sibling calls by label only when ownership and overload correspondence are unambiguous, preferring a declaration with a body. LCS aligns ordered siblings with deterministic tie-breaking. A uniquely paired key/signature change emits `~ ... (signature changed)` with both identities retained. Ambiguous overload pairing stays `-`/`+`; namespace collisions must never look like signature changes. Moves remain removal/addition in v1.

Keep `--context 2` unchanged siblings around each change; collapse other unchanged spans to `…`. Merge consecutive structurally identical siblings as `×N`. Reuse an earlier changed-member expansion with `↑ as above` only when its entire subtree is equivalent, including callback bodies and cycle/depth markers. Keep before and after expansions separate. With `--locs`, require matching displayed source locations before merging or reusing expansions. JSON retains every call site. Render active-path cycles with `↺` and depth limits explicitly. Compute change reachability before depth trimming so deeper changes produce “changes below depth limit”.

Sort roots and dispatch alternatives by canonical identity; preserve source order for calls. Tie-break partial-member locations by normalized path and span. Output uses LF, repo-relative paths, invariant culture, and no elapsed time or machine paths. ANSI is text-only on a TTY, honoring `NO_COLOR` and `--color auto|always|never`. Diff markers occupy column zero. Markdown wraps the same lines in a safe `diff` fence.

**CLI contract**

| Invocation | Meaning |
|---|---|
| `callrift` or `callrift diff` | HEAD → working tree |
| `callrift diff REV` | REV → working tree |
| `callrift diff BEFORE AFTER` | Two revisions |
| `callrift diff --from BEFORE --to AFTER` | Explicit revision form |
| `callrift diff --staged [REV]` | REV, default HEAD → index |
| `callrift diff main...HEAD` | merge-base(main, HEAD) → HEAD |
| `callrift tree [REV] -e OrdersController.Place` | One tree; default working tree |
| `callrift reach [REV] -e OrdersController.Place --to SqlOrderRepository.SaveAsync` | Potential simple paths to target |

Reject conflicting revision forms. Require a unique merge base in v1. For unborn HEAD, use an empty baseline. `--` separates optional path filters from revisions; paths filter selected changes/roots without removing dependencies from analysis.

Shared selection: repeatable `--entry/-e` and `--file/-F`. Resolve exact key, qualified label, then unique suffix; ambiguity lists candidates and exits. File selection accepts an exact repo path or unique suffix and selects callable definitions there, including private members. Multiple selectors form a union. `tree` and `reach` require selection; `reach` also requires `--to`.

Shared options: `--max-depth` with `--depth` alias (default 6), `--externals`, `--tests`, `--locs`, and `--format text|md|json`. Accept `markdown` as an alias for `md`. `--context` applies to diffs, with `all` disabling sibling elision. `--locs` shows root definitions and child call sites; JSON locations contain separate before/after values. `reach` adds `--max-paths` (default 100) and reports truncation. No path within a truncated search is not proof of no path.

Mode options: source-only by default; `--solution PATH` or `--project PATH` selects MSBuild. `--mode source|msbuild` makes the choice explicit; contradictory options fail. M3 adds `--framework`, `--configuration`, and `--no-restore` for already prepared snapshots.

Exit 0 for successful analysis, including an empty diff. `--exit-code` returns 1 for a detected change; invalid input or failed analysis returns 2. `--strict` returns 2 when analysis is incomplete. Diagnostics go to stderr and into JSON; stdout remains the requested artifact. Additive `callers`, YAML, JSONL, route-friendly minimal API labels, and dispatch plugins are deferred beyond M4.

**JSON v1 sketch**

The M2 schema describes the bounded semantic result before text-only elision and repeated-subtree compression. External/test selection and depth bounds still apply. Omitted descendants, unresolved bindings, and incomplete analysis are explicit. The following abbreviated example illustrates an added call, not the entire orders diff:

```json
{
  "schemaVersion": 1,
  "command": "diff",
  "from": { "kind": "revision", "ref": "HEAD", "commit": "<resolved-oid>" },
  "to": { "kind": "workingTree", "contentId": "<snapshot-digest>" },
  "analysis": {
    "mode": "source",
    "status": "partial",
    "limitations": ["missing-package-references", "possible-dispatch"]
  },
  "hasChanges": true,
  "truncated": false,
  "diagnostics": [],
  "trees": [{
    "id": "n0",
    "kind": "call",
    "change": "added",
    "label": "IAuditLog.RecordAsync → AuditLog.RecordAsync",
    "before": null,
    "after": {
      "symbolId": "source::Orders.IAuditLog.RecordAsync(Orders.OrderRequest)",
      "binding": "resolved",
      "dispatch": "possible",
      "targetIds": ["source::Orders.AuditLog.RecordAsync(Orders.OrderRequest)"],
      "callSites": [{ "path": "src/OrderService.cs", "startLine": 12, "startColumn": 9 }]
    },
    "children": [],
    "omission": null
  }]
}
```

Node kinds include member, call, branch, and dispatch target. Changes are unchanged, added, removed, or modified, with reasons such as signature, body, binding, and children. Each side carries its own symbol, signature, locations, and binding evidence; callback edges retain their relation. Full identities survive abbreviated labels. Locations are one-based; end positions are exclusive. Locations are always available in JSON; `--locs` controls human output.

Use deterministic traversal IDs and reference IDs for cycles. `omission` distinguishes depth limit, cycle, and path limit. The published schema also covers `tree` and `reach` envelopes (`paths` for reach). Do not infer success from an empty tree array: consumers must inspect analysis status, diagnostics, and truncation. Schema v1 permits additive optional fields; incompatible changes require a new major schema version. Publish `schemas/output-v1.schema.json` and snapshot the observable JSON contract in M2.

**Proposed file tree**

```diff
 callrift/
+├─ DESIGN.md
+├─ mise.toml
+├─ mise.lock
+├─ hk.pkl
+├─ global.json
+├─ callrift.slnx
+├─ Directory.Build.props
+├─ Directory.Packages.props
+├─ .editorconfig
+├─ .gitattributes
+├─ .gitignore
+├─ LICENSE
+├─ CONTRIBUTING.md
+├─ README.md
+├─ src/
+│  ├─ Callrift.Cli/
+│  ├─ Callrift.Core/
+│  │  ├─ Git/
+│  │  ├─ Analysis/
+│  │  ├─ Graph/
+│  │  ├─ Diffing/
+│  │  └─ Rendering/
+│  ├─ Callrift.MSBuild/
+│  └─ Callrift.WorkspaceWorker/
+├─ tests/
+│  ├─ Callrift.Scenarios/
+│  └─ Callrift.RealWorldCases/
+├─ benchmarks/
+│  ├─ Callrift.Benchmarks/
+│  └─ baselines/
+├─ real-world-cases/
+│  ├─ manifest.json
+│  └─ reviews/
+├─ tools/Callrift.RealWorldCases/
+├─ schemas/output-v1.schema.json
+└─ .github/workflows/
+   ├─ ci.yml
+   ├─ real-world-sweep.yml
+   └─ release.yml
```

Folders describe responsibility; add individual classes only when their milestone needs them. MSBuild projects arrive in M3; the schema arrives in M2. Repository setup is now present: Git, mise, hk, EditorConfig, LF attributes, and ignore rules. Application implementation remains pending design review.

**Development tools, dependencies, and contribution rules**

Use [mise](https://mise.jdx.dev/lang/dotnet.html) for pinned SDK and hk versions, environment setup, and shared tasks: restore, build, format-check, test, cases, benchmark, and pack. Keep `global.json` aligned because SDK selection can otherwise prefer another installed version. NuGet dependencies remain centrally pinned in `Directory.Packages.props`. Enable nullable, deterministic builds, and warnings as errors; use `.gitattributes` to keep snapshots LF.

Use [hk with mise integration](https://hk.jdx.dev/mise_integration) through repository-scoped `hk install --mise`. Pre-commit enforces EditorConfig and dotnet formatting; commit-msg enforces conventional commits. Add pre-push build and fast scenarios when M1 provides projects and tests. Hooks check without auto-accepting snapshots. Configure staged-content isolation explicitly and verify partial-staging behavior. CI invokes the same tasks. Downloads for real-world cases and full benchmarks stay outside ordinary commit hooks.

Use System.CommandLine for CLI parsing, System.Text.Json for serialization, Roslyn for analysis, xUnit with Verify.Xunit for snapshots, and BenchmarkDotNet with MemoryDiagnoser. Recheck the closure before adding or upgrading packages, including MSBuild packages. Preserve required dependency notices in distributed artifacts.

Place the complete AGPL text in `LICENSE` and set `PackageLicenseExpression` to `AGPL-3.0-or-later`. Add no README license section or source headers. The README will credit [calldiff](https://github.com/tanishqkancharla/calldiff) as inspiration and explicitly state independence and no affiliation. Its sequence will be purpose, striking example, installation, usage, implementation, and limitations.

A conventional CLA does not necessarily transfer copyright. The requested single-holder policy needs an assignment agreement; commercial relicensing can also be permitted by a suitably drafted CLA while contributors retain ownership. Before outside contributions are merged, identify the legal holder and approve the agreement that meets the intended policy. Document signing and enforcement in `CONTRIBUTING.md`, and configure [CLA Assistant](https://github.com/cla-assistant/cla-assistant) as a required check once repository creation is authorized. The service records assent; the agreement supplies the rights.

Inspect effective Git identity and signing configuration before authoring project repository commits, sign with the configured key, and stop if signing fails. Do not verify signatures or configure verification trust. Temporary fixture commits are throwaway test data and remain unsigned locally and in CI. Do not add signing requirements or signing-key setup for them. Fixture commit hashes are normalized only at the snapshot boundary.

**Milestones and acceptance**

```diff
 M1: CommandRunner.RunAsync
+ └─ DiffCommand.ExecuteAsync
+    ├─ SourceOnlyAnalysisProvider.AnalyzeAsync
+    ├─ CallriftService.DiffAsync
+    └─ TextWriter.Write / MarkdownWriter.Write
 M2: CommandRunner.RunAsync
+ ├─ JsonWriter.Write
+ ├─ TreeCommand.ExecuteAsync
+ ├─ ReachCommand.ExecuteAsync
~ └─ RevisionResolver.ResolveAsync
+    └─ GitRepository.FindMergeBaseAsync
 M3: IAnalysisProvider.AnalyzeAsync
+ └─ MSBuildAnalysisProvider.AnalyzeAsync
 M4: ReleaseWorkflow.Run
+ ├─ PackageBuilder.Pack
+ ├─ InstalledToolSmoke.Run
+ └─ ApprovedPublisher.Publish
```

| Milestone | Working deliverable and exit evidence |
|---|---|
| M1 | Source-only diff, revision/index/working snapshots, selectors, branch/callback/dispatch handling, text/markdown, and noise controls. All requested scenario families have reviewed snapshots. Initial real-world cases: five curated pairs each from two permissively licensed repositories. Floor benchmarks and source-mode diff baseline are committed. |
| M2 | Versioned JSON and schema, locations, tree, bounded reach, and merge-base syntax. Review JSON snapshots for every existing fixture and real-world case, plus command/revision snapshots. Add tree/reach floors and end-to-end baselines. |
| M3 | Isolated MSBuild loading and restore, real project references/defines/generated code, explicit workspace failures. Review parity and mode-specific snapshots. Add materialization, restore, workspace, generator, and accurate-mode end-to-end benchmarks. |
| M4 | Tool/library packages, local install and dnx smoke checks, README, three-OS Actions matrix, scheduled crash sweep, and release workflow. Review any output changes and refresh comparable benchmark baselines. Publishing remains a separate approval. |

Implement M1 as vertical slices: first a working direct-call diff through the real CLI, then DI/callbacks, branches and identities, then coverage/noise controls and real-world cases. Tests are explicitly authorized by this project brief. Use xUnit + Verify snapshots of CLI stdout/stderr/exit status, with text and Markdown recorded together per scenario. JSON snapshot coverage begins in M2 when the public format exists.

Scenario repositories cover primary-constructor and field DI; same-class calls, lambda wrapping, and method groups; new guards and signatures; overloads, extensions, and generics; local functions, conditional access, and await; records and partial classes; multiple implementations, abstract overrides, decorators, and recursion; top-level/minimal API callbacks; and test exclusion including `Latest`. Add behavioral cases for ambiguous binding, changed dispatch without changed caller syntax, body-only changes, staged versus unstaged content, and changes below the depth limit.

Generate each fixture repository in a temporary directory, commit before/after states, and invoke the CLI. Review every received snapshot against the scenario description. For real-world case snapshots, read `git show AFTER` before accepting; record the reviewer rationale beside the manifest entry. Never bulk-accept. Known-wrong snapshots need an issue link; before GitHub exists, use a local issue document that can later be linked to its public issue.

The manifest records URL, verified license and license-file reference, immutable before/after SHAs, rationale, tags, command options, and known issues. Start candidate investigation with Serilog and one small DI repository; verify licenses before inclusion. A separate developer command proposes non-merge commits touching roughly 1–30 C# files and excludes formatting-only/test-only candidates. Hand-curate five per repository first, then grow toward 5–20 per repository and add medium/large workloads.

Clone at test time into a cache outside the tracked tree with `--filter=blob:none --no-checkout`, then fetch pinned SHAs. Cache CI clones by manifest hash. Persist only tool output and review records. A scheduled crash sweep analyzes many recent commits and records exceptions/timings without accepting snapshots.

Every benchmark uses pinned real-world cases and MemoryDiagnoser. Measure listing, blob reads, parse, reference loading/compilation, symbol binding, dispatch mapping, body equivalence, expansion, LCS, rendering, and later workspace loading independently. Keep setup out of stage measurements; include it in end-to-end runs. Compare total time to the serial sum and the parallel critical path of stage floors. Use BenchmarkDotNet JSON exports with environment metadata, cold process and warm process runs, and small/medium/large workloads. State filesystem-cache conditions rather than claiming a portable cold disk cache.

Before each optimization, capture a baseline on the same workload and environment; follow with time and allocation comparisons and unchanged observable snapshots. Syntax-tree reuse must include blob ID, logical path, and parse options. Persistent caches must also include reference and analysis configuration. Binding prefilters require equivalence evidence before adoption. CI initially flags generous time/allocation regressions and uses same-run comparisons where practical. The reported prototype's 30 seconds for about 5,000 files is a target to reproduce, not a measured baseline for this implementation.

**Reference review**

Read [Mantere's post](https://oskrim.github.io/engineering/2026/08/02/call-stack-diffs.html), HumanLayer's [Program Design section](https://github.com/humanlayer/advanced-context-engineering-for-coding-agents/blob/main/wsff.md#program-design), and the [show-me article](https://www.humanlayer.dev/blog/show-me-skill). Their compact planning forms informed this proposal. The [call-graph planning article](https://dev.to/derangga/call-graph-planning-adapting-effects-mental-model-for-ai-558h) provides additional planning context.

Read calldiff's [README](https://github.com/tanishqkancharla/calldiff) and [C# extractor](https://github.com/tanishqkancharla/calldiff/blob/main/src/languages/csharp.ts). The extractor uses textual member keys, skips callbacks, and collects block bodies. This motivates semantic identities and callback containment here. Study is for interoperability and design; implementation will be original. Reviewed [roslyn-diff](https://github.com/randlee/roslyn-diff) for member-level diff scope and the [C# LSP plugin](https://claude.com/marketplace/plugins/csharp-lsp) as adjacent semantic tooling.

Read the [Roslyn repository](https://github.com/dotnet/roslyn), semantic analysis and MSBuild Locator documentation linked above, [tool packaging guidance](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create), [one-shot execution documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-exec), and [file-based app documentation](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps). Planned installation is `dotnet tool install -g callrift`; one-shot use is `dnx callrift -- diff main...HEAD`.

Both supplied X links could not be fetched. The supplied YouTube page exposed no transcript, so its talk was not independently reviewed. `wsff.md` supplies the overlapping material, as the brief permits. M1 has 29 scenario snapshots, ten real-world case snapshots, and 14 measured benchmarks. GitHub repository creation, pushes, and publication still require approval.
