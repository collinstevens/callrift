# callrift

See how C# changes rewire your code. callrift uses Roslyn to compare calls across Git revisions and render trees for code review.

```diff
  OrdersController.Place
  └─ IOrderService.PlaceAsync → OrderService.PlaceAsync
     ├─ OrderService.Validate
-    ├─ IPricingClient.GetPriceAsync → PricingClient.GetPriceAsync
+    ├─ IAuditLog.RecordAsync → AuditLog.RecordAsync
+    ├─ OrderService.WithTimeout
+    │  └─ IPricingClient.GetPriceAsync → PricingClient.GetPriceAsync
     ├─ new Order
     └─ IOrderRepository.SaveAsync → SqlOrderRepository.SaveAsync
```

Inspired by [calldiff](https://github.com/tanishqkancharla/calldiff). callrift is an independent project and is not affiliated with calldiff.

callrift targets .NET 11 and pins SDK `11.0.100-rc.1.26425.128`. The CLI and library require the .NET 11 runtime. No public NuGet release exists yet. Build and install from this checkout:

```sh
mise install
mise run pack
dotnet tool install --global callrift --version 0.1.0-preview.1 --source ./artifacts/packages
```

After a public release, installation will be `dotnet tool install --global callrift --prerelease`, or one-shot execution with `dnx callrift@0.1.0-preview.1 -- diff main...HEAD`. Local one-shot execution accepts `--source ./artifacts/packages` before `--`.

Run from the repository you want to analyze:

```sh
callrift
callrift diff main HEAD --entry OrdersController.Place
callrift diff --staged --format md
callrift diff main...HEAD --format json
callrift tree --entry OrdersController.Place --locs
callrift reach --entry OrdersController.Place --to PricingClient.GetPriceAsync
callrift diff master...HEAD --solution App.slnx --framework net11.0
```

| Selection | Comparison |
|---|---|
| No revisions | HEAD → working tree |
| One revision | revision → working tree |
| Two revisions | first → second |
| `main...HEAD` | merge base → HEAD |
| `--staged` | HEAD → index |

`--entry` selects an exact key, label, or unique suffix. Ambiguous names produce an error. `--file` selects member definitions; paths after `--` restrict changed members used to select affected roots. `--depth` defaults to 6 and `--context` to 2 unchanged siblings. `--externals` reveals metadata calls and `--tests` includes test projects. Output is deterministic text, fenced Markdown (`md`), or versioned JSON; text uses ANSI colors on a terminal. Run `callrift --help` for all options.

Source-only analysis reads Git objects without checkout, restore, or build. In partial clones, it batches missing input blobs from configured promisor remotes when supported by Git. Unrelated asset blobs remain unfetched. It binds source calls with BCL references, follows source interface implementations and virtual overrides, nests callbacks under their receiving calls, and preserves branch conditions. Automatic roots come from transitive callers in both revisions. Dispatch candidates are possible targets; nested callbacks are not proof of execution. Explicit base calls stay direct. Sealed receiver types and directly constructed receivers exclude unrelated overrides.

Record-class `with` expressions follow the compiler clone and copy constructor, including possible overrides and cross-project copies. Labels use `clone State`; copying does not rerun instance initializers. Record structs and anonymous objects retain value-copy behavior. See the [record-copy review](real-world-cases/reviews/record-copy.md) for validation and declaration limits.

Static field, property, and event initializers and explicit static constructors appear under `possible initialization of Type`. The graph preserves initializer order within a declaration, keeps closed generic types separate, and retains conditional initialization inside branches and callbacks. These paths describe possible initialization, not an exact runtime schedule. See the [static-initialization review](real-world-cases/reviews/static-initialization.md).

Missing package references remain visible as `?` calls and diagnostics. Use `--diagnostics full` for every diagnostic. JSON explicitly reports partial source-only coverage; `--strict` returns exit 2 for that coverage, even without binding errors. Single-compilation collisions, project defines, generators, property/indexer bodies, operators, events, and runtime framework conventions remain limitations. Dispatch does not track receiver values through variables or resolve DI registrations. Candidate sets can include [targets excluded by enclosing type guards](real-world-cases/issues/receiver-flow.md). Body edits with no visible edge change are reported explicitly. This output complements the source diff.

MSBuild mode uses restored per-project compilations, defines, and generated sources. It materializes revisions into an external cache and requires an installed compatible SDK. See [MSBuild analysis](docs/msbuild.md) for cache behavior and remaining coverage limits.

The reusable `callrift.core` package exposes the same source-only engine:

```csharp
using Callrift.Core;

var result = await new CallriftService().DiffAsync(
    new DiffRequest(repositoryPath, "main", "HEAD")
    {
        Options = new DiffOptions { Entries = ["OrdersController.Place"] }
    });
Console.WriteLine(JsonRenderer.Render(result));
```

The suite includes 32 feature scenarios, command/revision checks, pinned real-history changes, and workspace/package/generator checks. Snapshots are reviewed against their source changes. The [constructor and depth review](real-world-cases/reviews/constructor-initialization.md) records the latest corpus-wide changes and remaining initialization limits. BenchmarkDotNet baselines cover source stages and commands plus restored workspace operations on a small Serilog workload. Large-repository performance and finer workspace-stage floors are still unmeasured; the prototype's reported 30 seconds is not a callrift measurement.

See [JSON semantics](docs/json.md), [benchmarks](benchmarks/README.md), [release procedure](docs/releasing.md), [DESIGN.md](DESIGN.md), and [CONTRIBUTING.md](CONTRIBUTING.md).
