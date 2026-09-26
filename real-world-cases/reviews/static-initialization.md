# Static initialization and conditional calls

Static initializer work was absent from callers that read static state. A changed singleton constructor could appear as an isolated root, and a depth-limited call could hide initializer changes without reporting truncation. Treating `??=` as an unconditional assignment also hid its null guard and could suppress a later initializer even when the right operand was skipped.

The implementation indexes explicit static constructors and compiler-synthesized initializers. Static field, property, and event initializer expressions precede the explicit constructor body. Constant fields add no initialization edge. Trigger identities retain declaring project, file, and closed generic context. Initializers from separate partial declarations are grouped with an unspecified-order diagnostic; calls within each declaration retain source order.

## Possible execution and ordering

An access can expose `possible initialization of Type` followed by the source initializer call. Runtime initialization state is unknown, and types with `beforefieldinit` semantics permit flexible initialization timing. These branches describe potential work. They do not assert a runtime schedule.

A path suppresses repeated initialization of the same closed type. Each conditional branch starts with the enclosing initialization state, and separate callback arguments retain independent state. An initializer reached only in a skipped callback or branch cannot hide a later initialization possibility. Reads and writes preserve operand order: compound field assignment reads the field before its right operand; event subscription evaluates the supplied handler before event initialization. A null-coalescing assignment evaluates its receiver and index once, then places its right operand under a null guard.

Depth omissions account for initializer work that remains possible on the current path. Empty initializers and already completed initialization do not create false truncation. Source calls hidden by a depth bound retain an explicit omission. Unavailable or ambiguous initializer bindings remain diagnostics.

## Generated identities

OrchardCore generators emit interceptor classes with fresh names. Using those names in initializer identities produced false removed/added initializers and false pager roots. Initializer identities now derive from the sorted stable identities of the intercepted calls in their declaring type. Project and file scopes remain intact. Real source-call and initializer-body changes still appear in the diff.

## Reviewed corpus effects

- Polly's singleton initializer connects `TelemetryListenerImpl` and its callers to `TelemetrySource` construction. The formerly isolated private constructor root disappears. Other Polly views expose pool, pipeline, and `VoidResult.Instance` initialization beneath existing callbacks and depth bounds.
- Autofac exposes lifetime singletons and changed benchmark type lists. The null-coalescing cache in `BaseGenericResolveDelegateInvoker` now has a null guard. Existing calls and possible dispatch targets are preserved.
- Ocelot exposes static configuration keys and conditional defaults. `GuardSchema` creates global configuration only when its existing value is null. Adding the guard can place constructor work at the existing depth bound; the resulting omission is explicit.
- OrchardCore permission and JSON-default initializers retain source locations and constructor order. The generated-identity correction removes false pager roots. The OpenID source automatic view contains 227 initializer possibilities across 42 declaring types. Its restored automatic view adds two null guards around authorization creation arguments; all other prior JSON fields remain unchanged apart from traversal IDs.
- ASP.NET Core exposes dictionary, JSON, reflection, and singleton initializer work. Source diagnostics remain tied to the pinned declarations and incomplete source-only bindings.

The corpus review checks each added initializer against immutable source declarations and original compiler symbols. Exact comparisons preserve the remaining roots, targets, locations, diagnostics, and rendered output outside the enumerated changes. Null-coalescing reviews separately check each guard's source span and conditional child calls.

## Validation status

The latest candidate passes 12 source/restored null-coalescing CLI cases and six independent runtime-order probes. All 42 integrated workspace checks pass, including generated-identity cases that preserve unchanged output while exposing real call and literal changes.

The complete 89-view corpus run produced 72 passes and 17 snapshot differences. All 17 differences were independently reviewed against pinned source, then passed separate repeats with the reviewed expectations. Every final output is accounted for across JSON, text, and Markdown. The 121 changed snapshot files were promoted only after their source reviews, frozen input hashes, and passing repeats were checked. All 177 production snapshot files match those reviewed outputs. The original full run remains recorded as 72 passes and 17 differences, rather than a literal 89-pass run.

The integrated solution builds without warnings or errors. Formatting, EditorConfig, workflow validation, and package smoke checks pass. The complete isolated suite passes all 639 scenarios in 1h37m with frozen binaries, sources, snapshots, and review inputs unchanged. The integrated pre-push scenario run and final cross-platform CI remain pending. Results from earlier candidates are retained separately and do not establish a pass for this implementation.

Property/indexer and event accessor bodies, user-defined operators and conversions, collection-expression lowering, and broader compiler diagnostics remain open parts of the implementation audit. See the [initialization issue](../issues/initialization.md). The runtime probes and corpus reviews do not establish complete language or framework coverage.
