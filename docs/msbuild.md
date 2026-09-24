# MSBuild analysis

```sh
callrift diff main...HEAD --solution App.slnx --framework net10.0
callrift tree HEAD --project src/App/App.csproj --entry OrdersController.Place
callrift diff --project src/App/App.csproj --configuration Release --no-restore
```

`--solution` or `--project` selects MSBuild mode. `--mode msbuild` also requires one of these targets. Paths are relative to the invoking directory within the repository. A multi-target project requires `--framework`; callrift does not merge framework-specific graphs.

```diff
 CallriftService.DiffAsync
+  GitRepository.ReadSnapshotAsync(allFiles: true)
+  MSBuildAnalysisProvider.AnalyzeAsync
+    materialize immutable snapshot in external cache
+    dotnet restore
+    worker process → MSBuildLocator.RegisterDefaults
+      WorkspaceAnalysis.AnalyzeAsync
+        MSBuildWorkspace.OpenProjectAsync / OpenSolutionAsync
+        Project.GetCompilationAsync
         SourceOnlyAnalysisProvider.AnalyzeCompilation
   CallriftService.Compare
```

This mode restores packages, evaluates project targets, and runs source generators. Use it on code you trust. It writes snapshot files and restore artifacts to the user-local `Callrift/workspaces` cache, outside the checkout. Set `CALLRIFT_WORKSPACE_CACHE` to choose another directory. Cache entries contain source and assets; remove unused entries when you no longer need them. A per-entry lock serializes analysis. `--no-restore` requires a previously restored entry for the same snapshot, target, framework, and configuration.

The selected SDK must already be installed. Historical `global.json` settings apply inside the snapshot. Missing SDKs, failed restores, and workspace-loading failures return exit 2. Compilation errors become diagnostics; there is no silent source-only fallback. Symlinks and submodules in historical/index snapshots are rejected. Repository-external project imports, Git-dependent build tasks, and generators requiring compiled project outputs can require additional preparation and may fail.

Symbols use `project:<relative-project>@<framework>::<member>` identities. Project references connect compilations while duplicate type names in unrelated projects remain separate. Test exclusion uses evaluated `IsTestProject` and test-framework metadata references. Included generators contribute syntax trees and call bodies; generated paths are normalized relative paths.

The graph still describes possible calls. It does not infer a DI container's active registration, mediator handler conventions, non-abstract virtual overrides, property/indexer bodies, operators, events, or static initialization. Both modes therefore report partial coverage. In particular, a generated regex runner can appear as a root because its static singleton/property path is not followed. `--strict` returns exit 2 for partial coverage.

The worker and CLI share a version and communicate through temporary JSON files. Token fingerprints preserve body-only changes across that process boundary. The source-only provider retains Roslyn syntax equivalence. Whitespace and comments do not count as body changes in either mode.
