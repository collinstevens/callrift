# Preserving calls when dispatch targets change

Adding an implementation previously made an unchanged implementation appear removed and added. The single-target display used a compact call label, but multiple targets used an explicit contract with implementation children. The comparison treated that display change as a source change. An OrchardCore localization candidate exposed the problem when its interface grew from one implementation to eleven.

The comparison now expands both sides to the same contract/implementation structure when their dispatch shapes differ. It preserves the original invocation identity, declaration signatures, callbacks, and expansion depth. Empty, singleton, multiple-target, and replacement transitions retain the unchanged contract. Direct virtual bodies participate in the same comparison. Possible dispatch still describes candidates rather than proven runtime execution.

Single-self dispatch contracts remain available for receiver binding. A recursive implementation stays unchanged when another implementation is added. JSON cycle references point to the active implementation or caller ancestor, and dispatch containers do not replace those ancestors. Public schema version 1 and declaration identities remain unchanged.

## Independent expectations

The existing `dispatch-added` fixture adds only `Second.Save` to an interface already implemented by `First.Save`. Neither `Flow.Run` nor `First.Save` changes. The corrected text/Markdown snapshot preserves both and adds only `Second.Save`. The JSON review checks every other field and all nine definition/call-site locations against the literal before/after fixture sources. Only these two existing scenario snapshots change.

Twenty new CLI cases cover source and restored analysis for empty/single/multiple targets, replacements, explicit contract roots, direct virtual bodies, generic interfaces, conditional calls, callbacks, recursion, and depth limits. Each case runs in both revision directions. Four additional cases preserve recursive interface and virtual implementations and check that cycle IDs resolve to the expected ancestor. The twenty initial cases fail on the published receiver build; the four cycle cases fail on the intermediate prototype.

## Validation

The final isolated implementation passes all 403 scenarios in 55m5s and all 22 workspace cases in 6m15s. The scenario run includes the 24 new regressions but predates the separate four Git cancellation regressions. Thirty-eight API boundary checks and four self-cycle probes pass. All 42 JSON probe envelopes validate against schema version 1. Twenty-four baseline/current tree and reach comparisons are byte-identical across both modes, both revision pins, and text, Markdown, and JSON. Core and regression formatting pass.

The 89-view corpus run completed with 88 matches and one reviewed Serilog difference. The null-key case replaces exactly three depth-limit markers with cycle references to the same active visitor. Immutable source and invocation traces at both pins establish all three return paths through `VisitSequenceValue`, `VisitStructureValue`, and `VisitDictionaryValue`. All other JSON, source locations, diagnostics, and coverage are identical. Text and Markdown apply the existing unchanged-context window after those nodes stop reporting changes below depth. The focused repeat passes with the two reviewed expectations. Together the full run and repeat validate all 89 views.

The five implementation files, 24 regressions, two dispatch scenario snapshots, and two Serilog snapshots are integrated. The separate signed Git cancellation fix is preserved. All 38 focused dispatch and Git checks pass together using packaged Release binaries with the production suite's serialization setting. Formatting and package smoke pass, including the installed tool, restored worker, separate library consumer, and dnx. The combined 407-scenario push hook and CI remain pending. These concurrent correctness runs do not establish performance results.

The focused OrchardCore localization trial preserves the original Media implementation and adds only the ten new localizers in all three formats. The original implementation's immutable source blob is unchanged. Its resource-manifest tree, pins, analysis, and diagnostics remain identical to the baseline. This focused review does not accept that candidate: initialization omissions and its complete review remain open.
