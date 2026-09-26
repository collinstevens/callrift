# Constructors and accurate depth omissions

Default constructors were absent when their type had no executable initializer. Implicit base calls were also missing. Adding a class could therefore omit its constructor root, and a constructor could hide work in a source base type. Separately, reaching a depth bound marked empty member bodies as truncated. An empty reach result could report incompleteness even when no visible calls had been omitted.

The implementation indexes available implicit constructors, uses compiler binding for base overload resolution, and combines instance initialization with constructor bodies. A `this(...)` chain retains one initializer execution. Default struct construction and record copying skip instance initializers. Duplicate-body and extern omissions remain intact. Primary and implicit constructors use the same canonical void-return signature as explicit constructors. Implicit definitions and base call sites identify the original constructor or type declaration.

Depth omissions now require calls that would be visible under the selected external-call setting. Source calls, unresolved calls, possible callback bodies, and enabled metadata calls still produce truncation. Empty or hidden-only leaves do not. Existing callback children, guards, cycles, ordering, and dispatch targets remain unchanged.

## Independent corpus review

The complete 89-view run produced three unchanged matches and 86 snapshot differences. Every command exited successfully. All 86 changed views were reviewed against immutable Git source and original compiler symbols before copying the 157 changed snapshot files. The checks compare all other JSON fields and text/Markdown lines exactly after enumerating the specific additions, metadata updates, depth annotations, and diagnostics. Source verifiers use parsing and compiler APIs; they do not invoke the changed constructor binding or tree expansion implementation.

- Serilog exposes source base constructors and removes false depth markers. The combined restored snapshot received the same source-backed review as the separate JSON and rendered snapshots.
- Autofac adds 16 default-constructor roots across four automatic-root views. Original compiler symbols establish that these constructors were absent at the earlier pins. `Derived<T>` exposes its `WideBase` base. Focused views preserve the `KeyedService → Service → object` chain and the implicit deferred-map definition.
- Polly preserves original calls while adding constructor metadata and object bases. Source-base visibility explains the remaining depth annotations, including the private-protected pipeline-builder base constructor.
- Ocelot exposes object bases, canonical primary signatures, and default constructors. The new `DefaultInfo` root has only auto-properties, no instance initializers, and an object base; the type is absent before the JSON-merge change.
- OrchardCore automatic-root views preserve root and node counts. All 296 constructor metadata occurrences were checked against original declarations. Fourteen added depth markers have accessible source bases in `Document`, `Entity`, or `ContentPart`. The eight focused views passed 96 further constructor, base-location, and initializer-inventory checks. Workflow expansion exposes `ActivityRecord → Entity → object`.
- ASP.NET Core adds the default constructor of the new readonly record struct `AsyncInteropResult` beside its primary constructor. Twelve source checks verify its absence before the change, both constructor signatures, and the `VoidTaskResultGetter` object base. With externals enabled, deep callback occurrences retain real truncation; the shallower occurrence expands object.
- CleanArchitecture preserves existing roots and source calls while exposing unresolved base constructors. The corrected partial top-level `Program` declaration no longer produces a false base diagnostic.

New unresolved-base diagnostics were reviewed separately against missing/error base types or the absence of an accessible base candidate. ASP.NET Core has 204 valid additions and six removals from already-omitted duplicate constructors. The earlier false `Program` diagnostic is excluded. Existing diagnostic ordering and duplicate-member reporting are preserved. Restored analysis retains its original diagnostic sets.

The [initialization issue](../issues/initialization.md) records remaining collection-expression lowering and related semantic work. Reviewing these constructor deltas does not establish complete language or framework coverage.

## Validation

The frozen constructor/depth candidate passes all 491 scenarios and all 22 workspace cases. Four later reach regressions pass separately. The integrated suite has 495 scenarios, including 88 new constructor/depth regressions. Existing dispatch assertions now select the contract under test while independently checking additional constructor roots. Ten scenario snapshot changes have literal-fixture and field-by-field review.

All 89 corpus JSON envelopes validate against the published version-1 schema. The integrated solution builds without warnings or errors. Formatting and actionlint pass. Package smoke passes for local tool installation, source and restored analysis through the packaged worker, a separate library consumer, and dnx. All 108 focused integrated constructor, depth, and dispatch regressions pass in 17m6s. The isolated repeat passes all 89 corpus views in 56m15s with unchanged frozen inputs and no received snapshots. The full pre-push scenario hook and pushed CI results remain pending at checkpoint preparation. No performance claim follows from these concurrent correctness runs.
