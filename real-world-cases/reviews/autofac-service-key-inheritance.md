# autofac-service-key-inheritance

The subsequent [test-helper classification review](test-classification.md) records the current source counts and diagnostics. It supersedes the source counts below; restored and focused graph structures are unchanged.

Status: snapshots reviewed locally. Repeatability and cross-platform execution are tracked in [the shared review](autofac-expansion.md).

Repository: https://github.com/autofac/Autofac. Before: `cdf6a83b8e85eefb60e02b9056be51fe050a2fef`. After: `0f0be581167e22671f73de59e410e8d1552540e0`. Both `LICENSE` files contain MIT text at blob `e89fb634a4319b9488446ec6fb04e58cdbb1f2ad`; their identities and the license text were checked before generation.

The inspected diff replaces the constructor's inline GetOrAdd callback with UsesServiceKeyAttributeCached. UsesServiceKeyAttribute changes constructor/property reflection to DeclaredOnly, preserves both attribute-check loops, and adds a guarded base-type call through the cache helper. The new helper retains GetOrAdd and its callback to UsesServiceKeyAttribute. The resulting mutual recursion is a potential static cycle with a decreasing base-type argument; it does not prove infinite runtime recursion. Focused views must retain the baseType null/object guard and the preserved attribute calls.

Four views cover automatic roots at depth one and `ReflectionActivator.UsesServiceKeyAttribute` at depth 4, in source and MSBuild modes. The restored project is `src/Autofac/Autofac.csproj`, framework net10.0, using the externally materialized workspace and mise-provisioned SDK. Depth limits are explicit; completeness beyond those limits is not claimed. Source mode includes non-test benchmark and application inputs; restored mode selects the library and its generated code.

Both focused graphs have 25 nodes, four depth omissions, and one bounded cycle. The old reflection loops are removed and the DeclaredMembers loops are added with their attribute checks intact. The new guarded cache-helper path ends at the active UsesServiceKeyAttribute member. Text and Markdown now retain that distinct GetOrAdd callback after the [renderer fix](callback-rendering.md). Their JSON trees agree across modes after normalizing symbol namespaces.

The source automatic graph has 128 roots and 702 nodes. Compared with the reviewed held-pipeline graph, 123 roots retain their semantic contents. Four added benchmark roots cover ContainerBuildBenchmark.Build and the inherited-members benchmark constructor, setup, and build. An older ResolveKeyed overload is an additional affected root because the later benchmark callers are absent. The restored graph has 99 roots and 369 nodes and excludes benchmarks. Source mode reports 176 diagnostics, including duplicated before/after formatting diagnostics whose source lines moved in ReflectionActivator; restored mode reports zero. Shared validation and location evidence are recorded in [the expansion review](autofac-expansion.md).

The [receiver-context review](receiver-context.md) records the subsequent inherited-call changes, diagnostic updates, and source traces for this pair. Its current counts supersede the earlier snapshot counts above.
