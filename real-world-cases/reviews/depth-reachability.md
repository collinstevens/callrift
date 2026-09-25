# Depth-limit reachability cache

Automatic source-mode expansion of the ASP.NET Core header-recovery pair repeatedly searched the same graph below depth limits. The original full command exceeded its 600-second trial limit. That observation motivated isolated measurements; concurrent diagnostic trials are not used as speedup evidence.

The cache records reachability only for requested graph paths. A successful search caches its successful path. A failed complete search caches every visited member as unreachable. Negative results are not cached inside a partly explored cycle: in `A → B → A` with another edge `A → C`, a changed `C` must remain reachable from both `A` and `B`. An empty change set returns immediately. Searches use the same dispatch target enumeration as the original implementation.

The source and MSBuild CLI regression independently defines two entries into that cycle and a separate unaffected cycle. Both affected entries retain `changes below depth limit` in text, Markdown, and JSON. The unaffected entry is absent. JSON reports depth-limit omissions, truncation, and no diagnostics. Both tests also pass against the original implementation.

The new mutable cache requires synchronization because callers can share a `TreeExpander`. A regression expands 1,024 distinct roots in parallel for thirty repeats and checks their changed descendants. The original implementation passes. The unsynchronized cache fails with dictionary corruption. The final synchronized cache passes all 30,720 expansions.

Validation of the final implementation:

- All 58 targeted scenario, query, and depth-reachability tests passed, including both analysis modes and the concurrency regression.
- An independent exhaustive check of all directed three-member graphs and change sets passed 12,288 reachability cases.
- All eight complete sampled-tree hashes match the original, reverse-index, unsynchronized-cache, and final implementations in the controlled benchmark setup.
- The full ASP.NET Core header output matches the reverse-index diagnostic trial byte for byte: 64,782,522 bytes, SHA-256 `9c05be61fd66a3a3aa1c89e89c612747b0a1f39b7e545868ee4dd4571fe1b864`. That output has 1,569 roots and 22,486 diagnostics. Equality is a preservation check, not acceptance of those roots or diagnostics as semantically correct.
- Package smoke and formatting checks passed.

The selective algorithm passed all 51 accepted real-world checks and 22 workspace checks before synchronization was added. Those runs establish algorithm preservation; they are not reported as a second full run of the synchronized version. No accepted snapshots changed. The required pre-push scenario suite runs on the committed implementation.

The [benchmark report](../../benchmarks/README.md) separates isolated expansion results from outstanding complete-command measurements. The ASP.NET Core and OrchardCore benchmark pairs remain outside the accepted corpus pending source and snapshot review. The discarded variants and full exports are retained to document both the reverse-index overhead and the concurrency failure.
