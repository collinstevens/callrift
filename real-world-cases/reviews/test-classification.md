# Test-helper classification review

Review date: 2026-09-24. This update covers the five pinned Serilog pairs and the five pinned Autofac pairs already in the manifest. It adds no revision pairs.

An unrecognized owning project previously bypassed the conventional test-directory fallback. Test-helper libraries without their own test-framework references entered dispatch and caller discovery. The corrected source classifier preserves explicit non-test and application metadata, then uses conventional paths for unknown projects. Inferred exclusions emit `test-project-inferred`; `--tests` includes the helpers. MSBuild membership continues to use evaluated metadata.

Serilog's `test/TestDummies/TestDummies.csproj` is a library with a Serilog reference and no test marker. Its project blob is `7662a1a2bf8598ecaf9b8aed5ad0b857c8ac9ac5` across all selected revisions. The independently inspected Apache-2.0 license remains blob `37ec93a14fdcd0d6e525d97c0cfa6b314eaa98d8`.

Only the extra-arguments graph changes: two occurrences each of `DummyHardCodedStringDestructuringPolicy.TryDestructure` and `DummyReduceVersionToMajorPolicy.TryDestructure` disappear from interface dispatch. Their pinned definitions are in TestDummies at lines 12–16 and 5–15. The first returns a hard-coded scalar; the second handles a Version's major component. Both are test implementations. Removing those four nodes and their target identities from the previous JSON yields the new graph after ignoring traversal IDs. Its node count falls from 113 to 109. The other four Serilog graphs are unchanged. All five gain one classification diagnostic.

Autofac's two helper libraries are `Autofac.Test.Scenarios.LoadContext` and `Autofac.Test.Scenarios.ScannedAssembly`, under `test`. Their project blobs remain `2bf6f15288ed354b132f036280356cbd036a23b4` and `da4d0686a48d8101f46127d5ee84eaa778a61292` across every selected revision. Neither declares test metadata. The inspected MIT license remains `e89fb634a4319b9488446ec6fb04e58cdbb1f2ad`.

`LifetimeScopeEndingModule.Load` in the LoadContext helper calls `ContainerBuilder.RegisterBuildCallback`. Excluding that test caller reveals the public registration method as an automatic root. Each new shallow subtree contains the root, its `_buildCallbacks == null` guard, `BuildCallbackService` construction, `RegisterInstance`, and `AddCallback`. The source null-argument guard has no retained internal call under these options. All previous root subtrees remain identical after ignoring traversal IDs. Every focused graph is unchanged. Each source view gains two classification diagnostics; existing unresolved diagnostics remain intact.

| Autofac pair | Source roots | Source nodes | Source diagnostics |
|---|---:|---:|---:|
| held-pipeline | 133 | 737 | 174 |
| any-key-cache | 133 | 746 | 174 |
| service-key-inheritance | 129 | 707 | 178 |
| pipeline-callbacks | 127 | 694 | 178 |
| multi-service-registration | 134 | 738 | 178 |

These counts supersede the earlier source automatic counts in the individual reviews. The new root adds three depth omissions per view. Focused counts, cycles, restored counts, and all CleanArchitecture snapshots remain unchanged.

The review checked 40 new root locations against both pinned sides and 26 distinct revision/project/license combinations. All ranges fit their immutable files. JSON node IDs remain unique. Text and Markdown agree after removing fences; the only tree additions are the five independently reviewed registration subtrees. Serilog's removed targets were already below text context elision. The new classification diagnostics explain the input selection without claiming full build evaluation.

Nine CLI regressions cover both analysis modes, all three formats, `--tests`, literal overrides, conditional metadata, executable applications under test directories, executable test projects, and `Latest`. Their normal-output run passed (`Admin_COLLIN_2026-09-24_21_15_56_net11.0.trx`). Complete case repeatability and final cross-platform verification are recorded with the subsequent checkpoint evidence. No performance claim is made for this correctness change.
