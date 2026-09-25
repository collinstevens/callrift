# orchardcore-workflow-startup review

Accepted after independent source review, harness comparison, and repeat validation. All eight views across these two pairs matched the reviewed output in text, Markdown, and every JSON field. Sixteen snapshots are accepted. Cross-platform CI repeat remains pending.

Pinned pair: `0e50949dad6637c7e17214baeaa44df6f6be2dd8` to `ee892e7e9fb30305110504bf553dcf4265e6c02a` in [OrchardCore](https://github.com/OrchardCMS/OrchardCore). Both pins have BSD-3-Clause LICENSE blob `183936c000fa2cc5969ba724afc1bdc5bfae5080`. Source and restore assets remain in the external cache.

The new WorkflowTypeExtensions.HasStartActivity validates its receiver and calls Any with an IsStart predicate. IsMissingStartActivity validates the receiver and evaluates the helper only when Activities.Count is positive. ActivityController.Create POST computes the new record's IsStart from IsEvent and the negated helper before adding/saving the record. Its GET overload calls POST when the activity has no editor. WorkflowTypeController.Edit POST saves first, reports success, then warns under IsMissingStartActivity before redirecting.

Both automatic modes find precisely ActivityController.Create GET and WorkflowTypeController.Edit POST. The focused view deepens these same roots without replacing automatic discovery. Source mode cannot bind workflowType.HasStartActivity in the POST path; the diagnostic and unresolved node remain visible. The restored cross-project call resolves. HasStartActivity itself reaches the focused depth bound. The complete helper source establishes the independently expected Any predicate, but these snapshots do not display all helper internals.

The Razor list adds a warning badge using IsMissingStartActivity. Generated Razor execution is absent from this selected workspace graph. The six restored diagnostics originate in Liquid ShapePagerTag's explicitly dynamic objectValue calls. They do not indicate six unresolved calls in the changed workflow methods.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots | 2 | 27 | 16254 | yes |
| source-focused | 2 | 210 | 16254 | yes |
| msbuild-roots | 2 | 21 | 6 | yes |
| msbuild-focused | 2 | 178 | 6 | yes |

Both modes include automatic depth-one discovery and focused depth-three views with externals. Restored views select the named module project and references at net10.0; historical global.json selects SDK 10.0.401 with latestMajor roll-forward. Source mode uses the whole eligible repository C# input set and retains missing-package/build-input diagnostics. Coverage is partial in both modes. No potential path establishes runtime registration or execution.

The complete twenty-view Orchard trial audit checked unique traversal IDs, relative paths and side-specific UTF-16 source-location bounds, plus exact text/Markdown agreement after removing fences. These two pairs contain no generated locations. The source changes and surrounding methods were inspected independently of generated expectations. No CI repeat is claimed yet.

All eight views for these two pairs passed against the cancellation implementation in the 28-view addition replay. The 51 existing corpus checks passed separately against the same build. No snapshot contents changed during this replay.

The [generic-context review](generic-context.md) retains inherited display-driver instantiations separately. `ActivityDisplayManager` calls `DisplayManager<IActivity>`. The focused callback now contains 82 source and 31 restored instantiations of the same two implementation declarations, including 67 and 24 concrete activity types respectively. The remaining instances include open generic constraints and the common activity-model base context. Each repeated node is unchanged and depth-limited; all other tree fields agree after traversal-ID renumbering. Text/Markdown stdout and declaration target lists are unchanged. Source views gain 41 explicit total-state limit diagnostics. Restored diagnostics remain the same six dynamic-call diagnostics.

The [receiver-context review](receiver-context.md) records the subsequent inherited-call changes, diagnostic updates, and source traces for this pair. Its current counts supersede the earlier snapshot counts above.
