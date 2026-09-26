# Record copying

A record-class `with` expression could hide a changed copy constructor because the graph omitted its compiler-generated clone call. The graph now follows the bound clone through possible overrides to the copy constructor. Inherited and generic records retain receiver constraints. A directly constructed receiver excludes unrelated derived copies. Copying preserves the existing field values and does not rerun instance initializers; the `with` initializer expressions follow the copy.

Rendered labels use `clone State`. Canonical symbol IDs retain the compiler's `<Clone>$` identity. Clone definitions identify the record declaration, clone call sites identify the original `with` expression, and explicit copy constructors keep their original spans. Metadata imports use the same readable label even when Roslyn does not mark the imported clone implicitly declared.

Abstract clones have no direct body. Duplicate copy constructors, incompatible duplicate record declarations, and invalid copy-constructor accessibility produce diagnostics and omit the unavailable copy body. The accessibility checks distinguish sealed records from records that permit inheritance. These checks do not claim complete compiler-diagnostic coverage.

## Independent evidence

Nine compiler-valid fixtures were emitted and executed to establish copy execution, inherited and abstract dispatch, generic copying, receiver exclusions, and initializer order. Twelve compiler-checked accessibility cases cover public, protected, internal, private, protected-internal, and private-protected constructors on sealed and unsealed records. Six further declaration cases compare duplicate, missing-partial, valid-partial, and derived-copy behavior. Valid controls execute the independently expected sink calls.

The real CLI passes all 46 focused record-copy checks in source and restored modes, including direct and inherited project references, invalid declarations, text/Markdown/JSON presentation, and original-source locations. The complete candidate passes 541 scenarios and 22 workspace cases.

## Corpus review

The initial 89-view corpus run matched 86 views. The other three views changed only their diagnostics and the corresponding rendered diagnostic totals. All trees, paths, identities, locations, dispatch targets, omissions, and other fields remained exactly unchanged.

The two ASP.NET Core source views add duplicate-record diagnostics for `Todo` and `WeatherForecast`. Original compiler declarations at `983abcdf5d7f50f5c3c877ff120b1e975dead25f` and `5f5f31f9a84de3461271a56f726e9b532810b071` show two non-partial declarations for each name. The CleanArchitecture endpoint-groups view adds equivalent diagnostics for `CleanArchitectureUseCaseCommand` and `CleanArchitectureUseCaseQuery` at `f5ceb68cfdbc5ec1b884834a3c7e6eb115b4e196` and `ec549a4f2a5fd38cadb92bce67304acd41f84982`.

Eight checks parse the immutable Git source and query original compiler symbols without invoking clone analysis. Each check confirms both declarations, their locations, and compiler error `CS0101`. These collisions arise in source mode's combined compilation of independent samples and templates; they do not establish that the upstream projects fail to compile separately. The diagnostic points to the first original declaration in deterministic path order.

All six changed snapshot files were reviewed before the isolated repeat. The repeat passes all 89 views in 54m32s with frozen inputs unchanged and no received snapshots. All 89 JSON envelopes validate against schema version 1. The integrated Release solution builds without warnings or errors, formatting passes, and package smoke passes for the installed tool, packaged worker, separate library consumer, and dnx. All 46 integrated focused checks pass in 8m41s with unchanged parent and worker binaries. The checkpoint hooks and pushed CI remain pending at checkpoint preparation. These concurrent checks do not establish performance results.
