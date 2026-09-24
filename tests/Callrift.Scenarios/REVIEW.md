# M1 scenario review

Each accepted snapshot was read against its description and the before/after source in `ScenarioCatalog`. Text and Markdown, stderr, and exit status share one artifact per case. Fixture commits use the configured signing identity. No fixture source is derived from the analysis output.

| Scenario | Checked behavior |
|---|---|
| orders | Controller root, three interface dispatches, pricing removed from its former position and added under the timeout callback. |
| field-di | Field-injected interface resolves to the service implementation. |
| method-group | The callback and its transitive store call move beneath the wrapper. |
| guard | An unchanged call moves beneath the added condition. |
| signature | A unique root signature change uses one modified node and retains its child. |
| overloads-generics | The generic extension reaches the generic repository overload, not the integer overload. |
| locals-conditional-await | A local function retains the nullable receiver guard and awaited sibling call. |
| records-partials | Calls bind across partial declarations and retain the record constructor. |
| multiple-implementations | Both candidates remain under the interface; only the changed candidate expands. |
| abstract-recursion | Abstract dispatch reaches the concrete override. The unchanged recursive branch collapses as context. |
| top-level | The top-level root retains the route callback. The unavailable MapPost extension has an explicit unresolved marker and diagnostic. |
| body-only | A literal change produces a body marker despite unchanged call edges. |
| branches | The switch arm stays beneath try. Catch/filter and finally remain context; the unchanged loop is elided. |
| external-wrapper | An external wrapper stays visible because its callback calls source code. |
| overload-recursion | Two overloads with the same label expand without a false cycle. |
| depth | Changes beneath the depth bound are reported explicitly. |
| constructor-initializer | An implicit constructor follows a field initializer into the changed method. |
| new-recursion | A newly recursive edge has a bounded cycle marker. |
| dispatch-added | A previously unique implementation becomes two candidates despite unchanged caller syntax. |
| nameof | A compile-time name edit changes the body without inventing a runtime invocation. |
| uncalled-interface | A changed implementation remains a root when its interface has no source callers. |
| constructor-argument | A base-constructor argument edit changes the body while retaining the constructor edge. |
| inherited-abstract | A concrete class inherits an abstract contract's implementation through an abstract intermediate class. |
| generic-arity | Parameterless generic methods retain separate identities. Only the one-parameter overload changes. |
| decorator | Candidate dispatch includes a decorator forwarding through the interface. Expand-once suppresses its repeated interface subtree; it does not assert the runtime container builds a cycle. |
| tests-excluded | Project-level test classification excludes tests. The production directory `Latest` remains included. |
| tests-included | Explicit inclusion makes the test caller a root. |
| InvalidSelection | Ambiguous and missing selectors fail with exit 2 and actionable diagnostics. |
| WorkingTreeAndIndex | The staged guard appears in index mode; the reverted working copy reports no change. |
