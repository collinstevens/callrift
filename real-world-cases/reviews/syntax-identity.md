# Syntax spacing and literal contents

Review date: 2026-09-24. This correctness update adds no historical pairs.

Formatting a condition or unresolved fluent receiver previously changed its internal call key. For example, `ready && Check()` and `ready&&Check()` produced different branches despite equivalent syntax. Collapsing whitespace also erased spaces inside string literals. Both source and restored CLI modes reproduced these errors.

Syntax-derived identities now remove trivia and normalize token spacing. Literal contents remain intact. Complete conditions participate in comparison before display truncation. Six CLI regressions cover guards, foreach types, unresolved receivers, and significant literal whitespace in both modes and all three formats. All six failed before the fix and passed afterward (`Admin_COLLIN_2026-09-24_21_42_14_net11.0.trx`).

Review of the existing real-world outputs found 22 changed JSON snapshots and 17 changed text/Markdown snapshots. Every JSON difference is a label or diagnostic message. Root counts, node order, change marks, signatures, symbol IDs, dispatch targets, source locations, diagnostics counts, omissions, and cycles remain identical. The JSON schema is unchanged. Text and Markdown agree in all 17 affected rendered snapshots.

The Autofac changes remove line-break spacing from fluent registration, metadata-filter, generated-factory, and variance calls. Source diagnostics also stop embedding a generator's comment inside its fluent expression. Nullable casts receive canonical spacing; the 100-character decorator guard label ends at a slightly different point. The full condition and its source location remain available to comparison and review.

CleanArchitecture changes normalize unresolved fluent calls in its Aspire host, validation behavior, and TodoItems access. Serilog has one JSON-only change: normalized spacing in an open `ValueTuple<,>` type shifts a truncated guard label. Its text context had already elided that branch. No diagnostic was removed or silently resolved.

Independent source checks used the manifest's immutable Git objects. They covered Autofac's `MetadataFilterAttribute.cs` at line 239, `RegistrationExtensions.Decorators.cs` at line 225, `GeneratedFactoryRegistrationSource.cs` at line 53, and `ContravariantRegistrationSource.cs` at line 104; CleanArchitecture's `ValidationBehaviour.cs` at line 23; and Serilog's `PropertyValueConverter.cs` at line 329. These expressions preserve the reviewed LINQ operations, nullable/coalescing guard, and tuple-type condition. Existing license evidence and location audits remain applicable because revisions and locations are unchanged.

All 32 existing call-flow scenarios passed without snapshot updates (`21_42_32` TRX). All 20 workspace checks passed (`21_44_53` TRX), as did formatting and package smoke. After the reviewed snapshot updates, all 31 case checks passed a repeat run in 7 minutes 1 second (`21_52_45` TRX). Actual pushed CI verification remains pending. No performance improvement is claimed.
