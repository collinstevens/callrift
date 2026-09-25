# orchardcore-role-submission review

Accepted after independent source review, harness comparison, and repeat validation. All eight views across these two pairs matched the reviewed output in text, Markdown, and every JSON field. Sixteen snapshots are accepted. Cross-platform CI repeat remains pending.

Pinned pair: `91cc98c5daa4e786b5f1980f2c5242ff18d1776c` to `0e50949dad6637c7e17214baeaa44df6f6be2dd8` in [OrchardCore](https://github.com/OrchardCMS/OrchardCore). Both pins have BSD-3-Clause LICENSE blob `183936c000fa2cc5969ba724afc1bdc5bfae5080`. Source and restore assets remain in the external cache.

Create and EditPost each gain a submit argument. Their independently reviewed source bodies preserve authorization, role validation/update, success notifications, and default Index redirects. Create adds a guarded Edit redirect for SaveAndConfigure after successful role creation. EditPost adds a guarded Edit redirect for SaveAndContinue after update and notification. The Razor buttons supply the corresponding constants; implicit view execution is outside the graph.

Automatic discovery in both modes returns exactly these two methods. JSON retains distinct before/after signature identities and aligns the methods as signature changes. The restored automatic view hides metadata redirect calls by default; the focused view includes externals and exposes both new guarded RedirectToAction calls in their original order. The focused source view reports the redirect, RoleManager, and notification bindings it cannot resolve. The restored views have no diagnostics.

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots | 2 | 35 | 16281 | yes |
| source-focused | 2 | 53 | 16281 | yes |
| msbuild-roots | 2 | 17 | 0 | yes |
| msbuild-focused | 2 | 76 | 0 | yes |

Both modes include automatic depth-one discovery and focused depth-three views with externals. Restored views select the named module project and references at net10.0; historical global.json selects SDK 10.0.401 with latestMajor roll-forward. Source mode uses the whole eligible repository C# input set and retains missing-package/build-input diagnostics. Coverage is partial in both modes. No potential path establishes runtime registration or execution.

The complete twenty-view Orchard trial audit checked unique traversal IDs, relative paths and side-specific UTF-16 source-location bounds, plus exact text/Markdown agreement after removing fences. These two pairs contain no generated locations. The source changes and surrounding methods were inspected independently of generated expectations. No CI repeat is claimed yet.

All eight views for these two pairs passed against the cancellation implementation in the 28-view addition replay. The 51 existing corpus checks passed separately against the same build. No snapshot contents changed during this replay.

The [generic-context review](generic-context.md) adds 41 explicit total-state limit diagnostics to each source view. The two roots, every JSON call tree, and rendered stdout remain identical. Restored views remain unchanged. The source graph includes all eligible projects and exhausts its 65,536 additional-state budget; a focused selector does not hide this limitation.

The [receiver-context review](receiver-context.md) records the subsequent inherited-call changes, diagnostic updates, and source traces for this pair. Its current counts supersede the earlier snapshot counts above.
