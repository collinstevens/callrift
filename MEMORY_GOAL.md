# Memory allocation optimization goal

Status: ready to continue. The latest measured implementation is `f6b9bb3`, including allocation changes in `fb4d30d`.

Continue profiling and reducing Callrift's memory allocations until the remaining opportunities no longer justify their implementation complexity, correctness risk, or maintenance cost. Preserve the complete analysis and observable behavior. Treat each improvement as an experiment with a measured result.

## Current baseline

Measured on this Windows computer with .NET SDK `11.0.100-rc.1.26425.128`, Release configuration, on 2026-09-27. GB values use decimal units.

Repository: `C:\Users\Admin\projects\Facepunch\sbox-public`.

Pin the revision pair when reproducing the benchmark:

```powershell
callrift diff 35e64bd69642791477930bb682464527cf422b98 372c601f8332149851410d88f36c0184bcb988f1
```

The measured command used `HEAD~1 HEAD` when those names resolved to this pair. Preserve default options, including depth, context, test inclusion, and text output. Capture output to a file consistently for timing comparisons.

| Measurement | Latest result |
|---|---:|
| Total managed allocations in the profiling harness | 18.53 GB |
| Source analysis, both revisions | 9.81 GB |
| Context construction, both revisions | 5.06 GB |
| Tree expansion, both revisions | 2.66 GB |
| Peak process working set in the harness | 4.14 GB |
| Cumulative GC pause time in the harness | 13.86 seconds |
| Harness execution time | 55.91 seconds |
| Installed CLI execution time | 55.65 seconds |

These are individual run measurements, not statistical guarantees. GC pause time is included in elapsed time. Total allocations measure allocation traffic; peak working set measures resident process memory. A reduction in one does not establish a reduction in the other.

The workload has 5,086 / 5,093 source inputs, 71,133 / 71,212 invocation contexts, 945 changed members, and 657 roots. The rendered text has 1,506,560 characters. Its UTF-8 SHA-256 is `7390A960E3FD799A566C61BDAEBCF4FFCFB6E1ECFF4EC653BDE13714A704B3E6`.

Allocations have fallen from approximately 46 GB before the optimization work, through 36.5 GB and 20.8 GB, to 18.53 GB. Continue from the latest baseline. Do not repeatedly claim earlier savings as new improvements.

## Profiling and implementation loop

1. Inspect the current tree, instructions, and incoming changes. Preserve unrelated work, including the existing `mise.lock` edit. If production behavior has changed since the baseline, establish a new matched baseline before attributing performance or output differences to an optimization.
2. Capture allocation stacks with `dotnet-trace` and attribute costs to stages, types, and allocation sites. Use counters, heap dumps, or process dumps when needed to distinguish transient garbage from retained objects. Existing tools under `artifacts/diagnostics` are sufficient to start; no paid profiler is required.
3. Rank opportunities by measured allocation volume, expected savings, risk, and implementation cost. Investigate source analysis, invocation-context construction, and tree expansion first, unless a fresh profile points elsewhere. Follow expensive callers into Roslyn instead of assuming all allocations beneath them belong to Callrift itself.
4. Make one attributable change or a small coherent group. Prefer eliminating unnecessary work and intermediate objects. Evaluate caches and pools against their retained memory, lifetime, invalidation, concurrency, and cleanup costs.
5. Compare the same pinned workload before and after. Record total managed allocations, relevant stage allocations, peak memory, GC pauses, elapsed time, and output equivalence. Run timing measurements without tracing overhead and without competing benchmark or test workloads. Repeat measurements only when variance, a new change, or an unresolved concern warrants it.
6. Keep changes with worthwhile measured gains. Revert ineffective experiments. Check an appropriate existing smaller or differently shaped workload when an optimization depends on the benchmark's graph shape or could move costs elsewhere.
7. Run existing fast and affected integration coverage, commit a signed checkpoint, and push. Continue independent work while CI runs. Refresh the profile after substantial changes so the next decision follows the remaining bottlenecks.

Working evidence is already available under ignored directories:

- `artifacts/sbox-breakdown`: stage-measurement harness, allocation traces, output captures, and baseline results. `alloc-round2-final.jsonl` contains the latest untraced measurements.
- `artifacts/allocation-reader`: temporary TraceEvent-based allocation-stack reader and summaries.
- `artifacts/diagnostics`: local .NET diagnostic tools.

Inspect the harness before reuse. It compiles production sources alongside local copies of `SourceOnlyAnalysisProvider.cs` and `ContextGraph.cs`; refresh those copies when their production files change. The artifacts are local working evidence and may be absent in another checkout.

Previously unsuccessful experiments include semantic-model reuse, broad symbol-formatting caches, batched constructor compilation changes, alternative trivia removal, and cached compact syntax labels. Revisit them only when new profiling evidence or a materially different design supports another experiment.

## Correctness and validation

- Preserve dispatch resolution, generic constraints, receiver identity, initialization ordering, callback isolation, cycle handling, depth limits, diagnostics, cancellation, and deterministic output. Do not reduce scope, depth, coverage, or output detail to manufacture savings.
- Verify output against the matched baseline. Investigate differences; do not bulk-accept snapshots for a performance refactor. Text equivalence alone does not establish JSON memory behavior or correctness for every analysis mode.
- Keep caches bounded by appropriate lifetimes. When adding reuse across analyses, check repeated invocations for retained compilations, syntax trees, graphs, and stale results.
- Use `mise.exe` on Windows. Run `mise.exe run test:fast` and focused existing integration, workspace, case, or benchmark selections relevant to the changed area. Use full local E2E only when explicitly requested. Never add new committed tests without an explicit request; remove temporary diagnostic tests before committing.
- Work directly on `master`. Inspect Git identity and signing configuration before commits, sign every authored commit, use conventional messages, and push checkpoints regularly. Integrate concurrent work safely without overwriting it.
- Keep EditorConfig, formatting without restore, and commit-message checks as commit gates. Do not make pushes wait for builds or tests. Report pending and failed CI accurately, including failures that predate the optimization.
- Keep traces, experimental programs, and detailed measurements in ignored artifacts. Do not create additional reports or documentation. This goal file may receive a concise status update when the goal is achieved.
- After a validated checkpoint intended for local use, update the globally installed tool with a unique local package version and verify the installed command on the pinned workload.

## When further optimization is no longer reasonable

There is no arbitrary allocation target that ends this goal. Continue while profiles identify practical opportunities with meaningful measured benefit.

Consider an opportunity worthwhile when it produces a repeatable reduction of roughly 1% or more in total allocations, a material reduction in peak memory or GC pressure, or a smaller clear saving from a simple change in a frequently executed path. This is a decision guide, not permission to accept regressions or a requirement to pursue complex changes for tiny gains.

Stop only after a fresh profile and the latest experiments support all of the following:

- The largest remaining avoidable allocation sites have been investigated. Each has a concrete reason for deferral, such as a failed measured experiment, necessary output materialization, compiler-owned work, or disproportionate correctness and maintenance cost.
- Recent independent experiments show diminishing returns, and no untried, high-confidence candidate remains that is likely to clear the benefit threshold at reasonable cost. Do not declare completion merely because one experiment failed or one stage became harder to optimize.
- The retained improvements have passed relevant existing checks, preserved matched output, and avoided material runtime or retained-memory regressions. Remaining CI results and limitations are stated accurately.
- The final result records the achieved allocation reduction, peak memory and runtime observations, remaining dominant allocation sources, and the evidence for stopping. Keep that conclusion concise in the final response and this file's status; keep detailed evidence in ignored artifacts.

If further progress requires a major architectural change, changed product behavior, or new correctness coverage that is not authorized, describe the concrete opportunity and tradeoff. Treat that boundary as a decision requiring user input rather than silently expanding scope or claiming the remaining allocation cost is unavoidable.
