# MSBuild analysis

```sh
callrift diff master...HEAD --solution App.slnx --framework net11.0
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
+        dotnet msbuild -target:ResolveReferences
+        MSBuildWorkspace.OpenProjectAsync / OpenSolutionAsync
+        Project.GetCompilationAsync
         SourceOnlyAnalysisProvider.AnalyzeCompilation
   CallriftService.Compare
```

This mode restores packages, builds referenced projects, evaluates project targets, and runs source generators. Use it on code you trust. It writes snapshot files and restore artifacts to the user-local `Callrift/workspaces` cache, outside the checkout. Set `CALLRIFT_WORKSPACE_CACHE` to choose another directory. Cache entries contain source and assets; remove unused entries when you no longer need them. A per-entry lock serializes analysis. `--no-restore` requires a previously restored entry for the same snapshot, target, framework, and configuration. It still prepares project references, including compiled analyzer and generator projects.

The selected SDK must already be installed. Historical `global.json` settings apply inside the snapshot. Missing SDKs, failed restores, failed reference builds, and workspace-loading failures return exit 2. Compilation errors in the analyzed project become diagnostics; there is no silent source-only fallback. Workspace warnings remain visible in diagnostics. Symlinks and submodules in historical/index snapshots are rejected. Repository-external project imports and Git-dependent build tasks can require additional preparation and may fail.

Restore covers the project's declared frameworks. `--framework` selects the analyzed root variant; referenced projects retain their own compatible frameworks. MSBuild's resolved reference outputs select between multiple variants of a referenced project. A solution can include single-target projects with different frameworks. Multi-target projects that lack the requested framework must be selected through a compatible project reference or analysis fails explicitly.

Empty entries in a framework list are ignored, including a leading semicolon introduced by an operating-system condition. callrift supplies an intermediate targets file for workspace evaluation. Historical project files remain intact.

Symbols use `project:<relative-project>@<framework>::<member>` identities. Project references connect compilations while duplicate type names in unrelated projects remain separate. Test exclusion uses evaluated `IsTestProject` and test-framework metadata references. Included generators contribute syntax trees and call bodies; generated paths are normalized relative paths.

File-local types retain their declaring file in member identities and remain distinct during interface dispatch. Calls replaced by compiler-selected interceptors follow the replacement body. File-local interceptor identities derive from the intercepted calls' logical file, enclosing member, original target, normalized invocation syntax, and occurrence among identical calls. Generated private names therefore do not create unrelated roots on each run. Inserting a distinct call preserves existing interceptor identities; changing a call's syntax can change its identity. Arbitrary nondeterministic generator output is not canonicalized. Declaration and call-site locations continue to identify the actual source and generated syntax.

Cross-framework dispatch uses referenced types as seen by the consuming compilation. For example, a net10.0 caller of `IComparable<string>.CompareTo` can reach an implementation in a netstandard2.0 or netstandard2.1 project despite differing framework assembly identities. The implementation retains its original library project and framework identity in the graph. Nested implementation types follow the same rule.

The graph still describes possible calls. Source virtual overrides participate in dispatch across projects, including inherited interface implementations. Explicit and implicit base-constructor calls stay direct. The graph does not infer a DI container's active registration, receiver values stored in variables, mediator handler conventions, property/indexer bodies, operators, event accessors, collection-expression lowering, or static initialization. Both modes therefore report partial coverage. In particular, a generated regex runner can appear as a root because its static singleton/property path is not followed. `--strict` returns exit 2 for partial coverage.

Instance constructors include executable field, property, and event initializers, the bound base constructor, and the body. A `this(...)` chain runs initializers only in the target constructor. Default struct construction and record copying do not rerun instance initializers. Unbound implicit base calls remain visible as unresolved nodes and diagnostics. [Initialization audit limits](../real-world-cases/issues/initialization.md) distinguish this behavior from remaining lowering and record-clone work.

Constructed invariant generic contracts exclude incompatible implementations, including nested arguments and inconsistent substitutions for repeated type parameters. Closed covariant and contravariant arguments follow reference conversions through inheritance, interfaces, and arrays. Closed invariant bindings also enforce implementation constraints: reference/value types, unmanaged fields, public parameterless constructors, base types, interfaces, and ref-like allowances. Declaration identities remain unchanged across constructed uses.

Type and method arguments follow calls through generic bodies, inherited implementations, constructors, extensions, local functions, and possible callbacks. Expanding `Router<int>.Run` therefore excludes an incompatible nested `IHandler<string>` implementation. Root selection, tree expansion, and reach queries use the same invocation contexts. JSON symbol IDs still identify declarations.

Expanding generic recursion can require indefinitely growing contexts. Generic arguments and receiver specializations share a bound of 65,536 additional invocation states and 128 nodes per type shape. A `generic-context-limit` diagnostic names the exceeded bound. Coverage stays partial, affected expansions have explicit omissions, and the result reports truncation. A changing budget boundary does not itself establish a code change.

Calls on the containing instance retain their receiver through inherited helpers, explicit base calls, constructors, local functions, and callbacks. A sealed `Left` instance cannot enter a sibling `Right` override through a shared base helper. Direct object creation also preserves its exact type. Fields, parameters, delegate invocations, and user-defined conversions keep their own receiver boundaries. File-local receiver and generic argument identities use logical declaring paths when comparing revisions.

Open parameter relationships, inference through variance, and receiver values stored in variables remain conservative. Enclosing guards do not narrow receiver types. See the [generic caller substitutions evidence](../real-world-cases/issues/generic-context.md), [receiver-context review](../real-world-cases/reviews/receiver-context.md), and [receiver-flow issue](../real-world-cases/issues/receiver-flow.md).

Static receiver types also constrain candidates. Ordinary reference casts preserve the underlying class constraint, so an interface cast cannot introduce an unrelated implementation or a false self-cycle. User-defined conversions stop that inference because their result can be a different object. Assignments and enclosing type guards still require receiver-flow analysis.

Interface receiver types constrain object-method overrides as well. For example, `worker.ToString()` with an interface-typed `worker` excludes source classes that do not implement that interface. A cast through `object` retains a more specific underlying class constraint when one is available. Concrete interface methods declared on abstract classes remain possible targets even when no concrete subclass is loaded; consumers can inherit those method bodies.

The worker and CLI share a version and communicate through temporary JSON files. Token fingerprints preserve body-only changes across that process boundary. The source-only provider retains Roslyn syntax equivalence. Whitespace and comments do not count as body changes in either mode.
