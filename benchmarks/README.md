# Benchmarks

Run the M1 suite with the managed SDK:

```sh
mise run benchmark -- --filter '*Benchmarks*' --job short
```

The initial workload is `serilog-alignment-guard` from the real-world cases manifest: 216 C# files in the revision, including 113 under `src/`. Parsing excludes test projects. Git listing/blob floors include project metadata and test source because classification happens after snapshot loading. The cached clone has no checkout; setup fetches missing blobs before measurements.

`FloorBenchmarks` measures listing, blob reads, parsing, fresh reference loading, compilation construction with cached references, invocation binding on a fresh compilation, dispatch mapping, equivalence, expansion, alignment, rendering, and the complete diff service. `CommandBenchmarks` includes argument parsing and rendering in a warm process and in a fresh CLI process. Warm command allocations cover callrift; fresh-process allocation figures cover the benchmark harness, not the child runtime's heap.

These operations are not an additive partition. Binding also triggers lazy Roslyn work. The implementation-map benchmark includes symbol enumeration; equivalence includes bound-edge comparison. The service loads and analyzes two revisions in parallel, reuses references within the process, and includes revision-resolution Git processes. Its rendering cost is measured separately. Compare a pair's parallel critical path and these extra operations before attributing a gap to overhead.

ShortRun uses three measured iterations and is a baseline, not evidence of a speedup. Filesystem caches were warm; fresh-process measurements include runtime startup without claiming a cold disk cache. Keep the JSON exporter output in `baselines/`, including its environment metadata. Rerun before and after an optimization on the same machine and pinned real-world case revisions. Snapshot output must remain correct.

The M1 Windows baseline on a Ryzen 9 7950X3D, SDK 10.0.303/runtime 10.0.11 measured about 614 ms for the warm command and 1.23 s for a fresh process. The warm command allocated about 240 MiB. Binding and end-to-end timings had substantial variation across the three measurements; use longer runs before drawing optimization conclusions. The exporter files are [floors](baselines/m1-windows-floors.json) and [commands](baselines/m1-windows-commands.json).

M2 adds JSON rendering and tree/reach query floors. On the same workload and environment, warm diff/tree/reach commands measured approximately 607/454/460 ms; a fresh diff process measured 1.22 s. These are feature baselines, not speedup claims. See [M2 floors](baselines/m2-windows-floors.json) and [M2 commands](baselines/m2-windows-commands.json). Exported JSON is preserved with whitespace normalized to the repository's formatting rules.

M3 adds [workspace measurements](baselines/m3-windows-workspaces.json) on the same pinned Serilog change, targeting its `net10.0` project. Capturing all project files costs 153 ms. Materialization, worker startup, workspace opening, generators, binding, and collection together cost 2.87 s for one revision. Accurate diff/tree/reach measure 3.32/3.10/3.11 s with restored caches. Restore and network fetches occur in setup. Allocations cover the parent process only; the worker heap is excluded. These aggregate workspace measurements are not isolated floors for workspace opening or generators.

M4 adds fresh-process tree/reach measurements and refreshes the [command baseline](baselines/m4-windows-commands.json). Warm diff/tree/reach measure 655/475/493 ms; fresh-process commands measure 1.26/1.06/1.07 s. Compared with M2, matching means are 1.04–1.08x and allocations 0.98–1.03x. ShortRun's error intervals are wide; these results do not establish a performance regression or improvement.

The scheduled benchmark workflow flags time above 2x or allocations above 1.5x the checked-in baseline. Warnings request review rather than failing on different hosted hardware. Exported measurements remain artifacts for investigation.

Medium/large workloads, finer workspace-stage floors, and cold restore measurements remain follow-up work. The prototype's 30 seconds for about 5,000 files has not been reproduced by this small workload.
