# Benchmarks

Run the M1 suite with the managed SDK:

```sh
mise run benchmark -- --filter '*Benchmarks*' --job short
```

The initial workload is `serilog-alignment-guard` from the corpus manifest: 216 C# files in the revision, including 113 under `src/`. Parsing excludes test projects. Git listing/blob floors include project metadata and test source because classification happens after snapshot loading. The cached clone has no checkout; setup fetches missing blobs before measurements.

`FloorBenchmarks` measures listing, blob reads, parsing, fresh reference loading, compilation construction with cached references, invocation binding on a fresh compilation, dispatch mapping, equivalence, expansion, alignment, rendering, and the complete diff service. `CommandBenchmarks` includes argument parsing and rendering in a warm process and in a fresh CLI process. Warm command allocations cover Callrift; fresh-process allocation figures cover the benchmark harness, not the child runtime's heap.

These operations are not an additive partition. Binding also triggers lazy Roslyn work. The implementation-map benchmark includes symbol enumeration; equivalence includes bound-edge comparison. The service loads and analyzes two revisions in parallel, reuses references within the process, and includes revision-resolution Git processes. Its rendering cost is measured separately. Compare a pair's parallel critical path and these extra operations before attributing a gap to overhead.

ShortRun uses three measured iterations and is a baseline, not evidence of a speedup. Filesystem caches were warm; fresh-process measurements include runtime startup without claiming a cold disk cache. Keep the JSON exporter output in `baselines/`, including its environment metadata. Rerun before and after an optimization on the same machine and pinned corpus revisions. Snapshot output must remain correct.

The M1 Windows baseline on a Ryzen 9 7950X3D, SDK 10.0.303/runtime 10.0.11 measured about 614 ms for the warm command and 1.23 s for a fresh process. The warm command allocated about 240 MiB. Binding and end-to-end timings had substantial variation across the three measurements; use longer runs before drawing optimization conclusions. The exporter files are [floors](baselines/m1-windows-floors.json) and [commands](baselines/m1-windows-commands.json).

Medium/large workloads, tree/reach commands, MSBuild loading, and automated regression thresholds belong to the following milestones. The prototype's 30 seconds for about 5,000 files has not been reproduced by this small workload.
