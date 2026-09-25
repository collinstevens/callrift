# Delegate-copy construction

Review date: 2026-09-24. This correction adds no accepted real-world pairs.

The inspected [OrchardCore](https://github.com/OrchardCMS/OrchardCore) logout pair is `d41228cd63be19b4cba09cad577bf711364acf16` to `9866770c0e90e1e2b60681922e247fda7ec4ec01`. Both revisions have the inspected BSD-3-Clause LICENSE blob `183936c000fa2cc5969ba724afc1bdc5bfae5080`. Source and restored assets remain outside this checkout.

The restored OpenId module trial reported two unresolved delegate constructions in `VolatileDocumentManager<TDocument>.UpdateAtomicAsync`. Independent inspection confirmed that `new UpdateDelegate(updateAsync)` copies a `Func<Task<TDocument>>`, and `new AfterUpdateDelegate(afterUpdateAsync)` copies a `Func<TDocument, Task>`. Both signatures match their destination delegates. These calls compile; they do not identify the eventual callback implementations.

A standalone fixture reproduced the incorrect diagnostic in both source and restored modes. Independent Roslyn 5.9 inspection confirmed that a delegate parameter and a factory-returned delegate produce valid `IDelegateCreationOperation` values. An incompatible return type, an unknown name, and a null operand instead produce invalid operations. The collector previously accepted only anonymous-function and method-reference operands. It now accepts any valid delegate-creation operation while retaining the named delegate type check.

Four new CLI cases failed before the correction: parameter and factory operands in both modes. Two incompatible-delegate cases already passed and remain passing. All fourteen new and existing delegate checks pass after the correction (23:30:34 run). Text, Markdown, and JSON show resolved construction; JSON confirms that factory evaluation precedes construction and that copying a value does not invent callback children. Existing lambda, method-group, diff/tree/reach, and unknown-target checks remain passing.

All four exploratory logout views were repeated: automatic and focused roots in source and restored modes. Each changes only by removing the two incorrect diagnostics. The complete remaining JSON, including graph structure, identities, locations, ordering, and omissions, is identical. Source views retain 16,281 diagnostics; restored views have none. Packaging and formatting checks pass.

All 51 accepted real-world checks passed unchanged with the corrected collector and matching workspace worker (23:31:14 run, 16 minutes 35 seconds). No accepted snapshot was updated for this correction.

This correction does not track delegate values through assignments or prove execution of an unknown callback. Complete OrchardCore snapshot acceptance remains separate from this targeted diagnosis.
