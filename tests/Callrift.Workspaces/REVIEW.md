# Workspace snapshot review

Each accepted snapshot was compared with its fixture description and edits. JSON was reviewed for project identity, side-specific changes, dispatch, locations, diagnostics, and deterministic paths.

- `orders`: the controller reaches the service through its interface; audit is added; pricing moves under the timeout callback; validation, construction, and persistence remain.
- `guard`: the same save call moves beneath `if (ready)`.
- `signature`: one modified root retains the save subtree and both signatures.
- `overloads-generics`: extension normalization selects the generic repository overload.
- `records-partials`: the partial class retains its check and implicit record construction around the added audit.
- `projects`: only project A's worker expands under App; project B's identical full type name never joins that path.
- `package-binding`: restored DI package return types bind the inferred local to `IWorker`; the interface expands to the source worker.
- `defines`: FEATURE selects Before/After; Hidden is absent.
- `generator`: SDK regex output changes the regex constructor and search body. Both are body-only modifications. The runner is a separate root because property/static-field paths are outside current coverage; the snapshot records that limitation explicitly.

All workspace snapshots include text, Markdown, and schema-version-1 JSON. Restore reuse is exercised by the second and third invocations. The SDK pin controls generated source and line spans.
