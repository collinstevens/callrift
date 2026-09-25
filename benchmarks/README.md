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

The .NET 11 suite pins BenchmarkDotNet 0.16.0-preview.2. Version 0.15.8 fails during runtime recognition on net11.0. Use the same BenchmarkDotNet package and managed SDK for before/after comparisons; older .NET 10 exports are historical baselines.

`ColdGitBenchmarks` reads twenty pinned production C# blobs from each selected Serilog and Polly revision. Every iteration creates a fresh shallow, blob-filtered client in the external benchmark cache. Its server is the existing local repository cache. Setup and cleanup are outside the measured blob-read operation, and cleanup verifies exact source contents. The client starts without blobs; server objects and filesystem caches are warm. This local-transport workload does not measure internet latency or a cold disk. MemoryDiagnoser reports managed parent allocations and excludes Git subprocess heaps.

For controlled comparisons, run without concurrent validation or build work:

```sh
mise run benchmark -- --filter '*ColdGitBenchmarks*' --warmupCount 3 --iterationCount 15 --launchCount 1 --invocationCount 1 --unrollFactor 1
mise run benchmark -- --filter '*FloorBenchmarks.ReadBlobs*' --warmupCount 3 --iterationCount 15 --launchCount 1
```

Run each command against both implementations with identical benchmark code, dependencies, pins, and cache conditions. The existing FloorBenchmarks blob reader measures the warm-input cost. Preserve full JSON exports and implementation identities before drawing a performance conclusion.

The controlled Windows Git comparison uses the original reader at `f0e83a85c79be088ba77cbe7083e025aaf68ed83` and the same .NET 11/BenchmarkDotNet environment for every variant. Each workload requested fifteen measurements after three warmups; BenchmarkDotNet retained twelve to fifteen measurements after its outlier filtering. Values below are means with one standard deviation. Full environment details, source hashes, pinned paths/object IDs, commands, run order, and links to all seven exports are in the [measurement metadata](baselines/git-batching-windows-metadata.json).

| Blob-read workload | Original reader | Final batched reader | Managed parent allocation, original → final |
|---|---:|---:|---:|
| Warm Serilog, 224 inputs | 206.45 ± 5.31 ms | 205.18 ± 3.81 ms | 5,969,717 → 6,051,413 bytes |
| Cold Serilog client, 20 inputs | 3,632.73 ± 69.57 ms | 389.73 ± 9.84 ms | 651,240 → 1,257,056 bytes |
| Cold Polly client, 20 inputs | 5,592.83 ± 77.89 ms | 500.68 ± 22.50 ms | 510,728 → 1,118,608 bytes |

Batching reduced this local-transport cold blob stage by approximately 9.3x for Serilog and 11.2x for Polly. The warm-time confidence intervals overlap; the experiment establishes no material warm-time change. Managed parent allocation increases by about 82 KB for the warm operation and 606–608 KB for the cold operations. The extra process environment, missing-input bookkeeping, and fetch/probe output contribute to these allocations; native Git heaps remain unmeasured.

An initial version probed every blob before reading. It measured 278.79 ms warm against an initial 199.60 ms baseline, a 39.7% penalty. The final version reads available blobs first with child-process lazy fetching disabled, then probes and hydrates only missing inputs. A repeated baseline measured 206.45 ms before the final 205.18 ms run. The initial version's complete exports are retained so the overhead investigation is reproducible; reconstruct it by applying the [initial reader patch](baselines/git-batching-probe-first.patch) to the baseline with `git apply --unidiff-zero`, using the same benchmark code and dependencies. These measurements establish a Git-stage improvement only. They do not establish full-command speedups, network performance, or the required large-repository analysis result.
