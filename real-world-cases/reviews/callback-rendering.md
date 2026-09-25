# Distinct callback rendering

Review date: 2026-09-24.

The provisional Autofac service-key inheritance pair exposed a rendering defect. Two `GetOrAdd` calls shared a method identity but carried different callbacks. Text and Markdown marked the second expansion `as above`, hiding the recursive base-type scan that JSON already contained. Repeated expansions now require equivalent subtrees. With `--locs`, equivalence also requires matching displayed source locations.

Six CLI regressions cover distinct inline lambdas, converted method groups, and repeated calls on separate lines in both source and restored-project modes. The distinct-callback fixtures independently require both before/after target pairs in text, Markdown, and JSON. The location fixtures require both call lines. Both defects were reproduced before the rendering fix.

Three existing text/Markdown snapshots changed. Their updates were reviewed against the existing JSON graphs and source:

- `decorator`: the nested interface expansion now exposes the bounded `Decorator.Run` cycle and changed `Worker.Run` body. The outer worker expansion can then reuse that same body. The fixture contains an interface call inside the decorator; the displayed cycle describes a static candidate, not a proven runtime loop.
- `autofac-held-pipeline-source-roots`: the nested-scope benchmark retains its task callback and nested resolve calls. The two decorator registrations retain their different constructor callbacks. Lazy metadata registration retains its resolve, request construction, and delegate construction. The AOT program retains distinct resolve/check callbacks, including parameterized, keyed, and child-scope variants.
- `autofac-held-pipeline-msbuild-roots`: lazy metadata registration retains the same callback. Generated registration overloads retain each distinct `DelegateInvoker` callback target instead of reusing the first overload's callback.

Autofac source review used immutable after revision `ae9e1e1129b9c22e7ab111381308dcb02f80a8d2`: `ConcurrencyNestedScopeBenchmark.cs`, `KeylessNestedLambdaBenchmark.cs`, `LazyWithMetadataRegistrationSource.cs`, `test/Autofac.Test.Aot/Program.cs`, and `DelegateRegisterGenerator.cs`. The generator emits twenty invoker variants with their corresponding `ResolveWithDelegate` method groups. These expansions were already present in the accepted JSON; no call graph, root, diagnostic, or truncation change is intended.

All sixteen JSON snapshots for the four provisional Autofac pairs remained byte-for-byte unchanged after regeneration with the renderer fix. Those pairs remain under review and are not accepted by this rendering checkpoint. The fix changes presentation only; no performance improvement is claimed.

Validation: all six new regressions pass (`20_27_09` TRX), all 34 scenario snapshot cases pass (`20_28_12`), and all 15 accepted real-world checks pass (`20_28_13`). The accepted JSON snapshots are unchanged. The solution build, formatting checks, and workflow validation pass. The full pre-push scenario suite and actual three-OS CI remain checkpoint gates.
