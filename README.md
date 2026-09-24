# Callrift

See how C# changes rewire your code. Callrift uses Roslyn to compare calls across Git revisions and render trees for code review.

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

Inspired by [calldiff](https://github.com/tanishqkancharla/calldiff). Callrift is an independent project and is not affiliated with calldiff.

The project is in development. NuGet installation and `dnx` distribution are planned for M4. For now:

```sh
mise install
mise run build
dotnet run --project src/Callrift.Cli -- diff
dotnet run --project src/Callrift.Cli -- diff main HEAD --entry OrdersController.Place
dotnet run --project src/Callrift.Cli -- diff --staged --format md
dotnet run --project src/Callrift.Cli -- diff main...HEAD --format json
dotnet run --project src/Callrift.Cli -- tree --entry OrdersController.Place --locs
dotnet run --project src/Callrift.Cli -- reach --entry OrdersController.Place --to PricingClient.GetPriceAsync
dotnet run --project src/Callrift.Cli -- diff main...HEAD --solution App.slnx --framework net10.0
```

The CLI analyzes the current directory's Git repository. To analyze another repository, run the built `callrift.dll` from that repository. With no revisions, it compares HEAD with the working tree; one revision compares that revision with the working tree; two revisions compare each other. `--staged` reads the index. `--entry`, `--file`, `--depth`, `--context`, `--externals`, and `--tests` control the output. Run with `--help` for options.

Source-only analysis reads Git objects without checkout, restore, or build. It binds source calls with BCL references, follows source interface/abstract implementations, nests callbacks under their receiving calls, and preserves branch conditions. Automatic roots come from transitive callers in both revisions. Calls through interfaces are possible targets, and nested callbacks are not proof of execution.

Missing package references remain visible as `?` calls and diagnostics. Use `--diagnostics full` for every diagnostic. JSON explicitly reports partial source-only coverage; `--strict` returns exit 2 for that coverage, even without binding errors. Single-compilation collisions, project defines, generators, virtual non-abstract dispatch, property/indexer bodies, operators, events, and runtime framework conventions remain limitations. Body edits with no visible edge change are reported explicitly. This output complements the source diff.

MSBuild mode uses restored per-project compilations, defines, and generated sources. It materializes revisions into an external cache and requires an installed compatible SDK. See [MSBuild analysis](docs/msbuild.md) for cache behavior and remaining coverage limits.

Text, Markdown, versioned JSON, locations, tree/reach queries, merge-base revisions, and MSBuild analysis are implemented. Distribution and CI are M4. See [JSON semantics](docs/json.md), [DESIGN.md](DESIGN.md), and [CONTRIBUTING.md](CONTRIBUTING.md).
