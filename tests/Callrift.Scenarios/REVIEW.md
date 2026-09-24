# M1 scenario review

Each accepted snapshot was read against its description and the before/after source in `ScenarioCatalog`. Text and Markdown, stderr, and exit status share one artifact per case. Local fixture commits use the configured signing identity; GitHub Actions creates unsigned temporary fixture commits. No fixture source is derived from the analysis output.

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

# M2 review

The JSON counterpart of every source scenario and the working/index case was reviewed against the same independent source edits. Checks covered before/after symbol identity, generic arity, callback relationships, possible dispatch targets, declaration versus invocation locations, and explicit cycle/depth omissions. The JSON keeps unchanged bounded subtrees even when text collapses them. Documents passed `schemas/output-v1.schema.json` through PowerShell's `Test-Json`.

The added recursive-signature scenario changes the root signature and removes a recursive branch. Its old call retains the old symbol ID and references the matched root's traversal ID. The decorator's cycle references the ancestor implementation rather than the intervening interface dispatch.

| Query snapshot | Review decision |
|---|---|
| query-tree | Accept. Full orders tree includes audit and pricing beneath the timeout. Root locations point at declarations; child locations point at invocations. |
| query-reach | Accept. The controller-to-pricing path includes interface dispatch and the timeout callback wrapper. |
| query-reach-limit | Accept. One switch-arm path is returned; the additional catch path triggers explicit truncation. |
| query-reach-depth | Accept. The depth bound prevents reaching pricing; the empty result is explicitly truncated. |
| query-no-path | Accept. Save cannot reach its caller and the search is not truncated. |
| query-tree-cycle | Accept. The cycle refers to the root traversal ID; the ordinary Save sibling remains. |
| query-diff-locs | Accept. The target orders diff has call-site locations and change exit status 1. |
| query-strict, query-strict-clean | Accept. Partial source-only coverage returns exit 2 with an explanation, both with and without unresolved bindings. |
| QueryTests.MergeBase | Accept. The unrelated divergent mainline class is absent; the comparison uses the common ancestor and the feature tip. |
| QueryTests.InvalidQueries | Accept. Missing selectors/target, invalid path limits, conflicting staged/range syntax, and multiple tree revisions fail with exit 2. |

# Contract-entry regression review

`DispatchQueryTests` exercises the CLI process in source and MSBuild modes, with text, Markdown, and JSON. Selecting an interface or abstract declaration previously skipped implementation expansion. The independent fixture expectation is a path from the selected contract through each possible source implementation to `Worker.After`, plus the removed `Worker.Before` call in diffs. Single- and multiple-implementation cases preserve the contract identity, declaration relationship, empty root call-site list, and explicit possible target set in JSON. A separate parameter-type change must produce one modified contract root with both identities and a signature-change detail.

All ten regression cases passed in `dispatch-query-final_net10.0_20260924153315.trx`. Existing snapshots were not rewritten for this fix.
