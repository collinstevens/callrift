# OrchardCore OpenID logout review

Status: reviewed and accepted. All four source/restored automatic/focused views passed an independent replay in 7m30s.

The pair compares `d41228cd63be19b4cba09cad577bf711364acf16` with `9866770c0e90e1e2b60681922e247fda7ec4ec01` in `OrchardCMS/OrchardCore`. Both pins use BSD-3-Clause license blob `183936c000fa2cc5969ba724afc1bdc5bfae5080`. The production C# changes affect the access controller, identifier extensions, server-settings recipe, recipe model, and deployment source. The added upstream tests were not treated as independent proof of callrift's output.

The controller extracts `SignOutAndRedirectAsync` from `LogoutAccept` and calls it from both the confirmed and matching-hint logout paths. The extracted body preserves cookie sign-out, the empty post-logout URI check, the root redirect, authentication-properties construction, and the framework sign-out call. The matching-hint path changes its success and nullable-principal guards, obtains a nullable user identifier, checks that both identifiers are nonempty, and retains the span conversion and fixed-time comparison. The throwing `GetUserIdentifier(ClaimsPrincipal)` wrapper now delegates to the new nullable `FindUserIdentifier`; the three claim fallbacks remain in order.

Recipe import and deployment export add `RequireEndSessionConfirmation` assignments, and the recipe model defaults the new property to true. These data changes make the corresponding handler bodies affected even when their visible calls remain unchanged. The graph does not model implicit property initialization as a separate executed call.

| View | Roots | Nodes | Diagnostics |
|---|---:|---:|---:|
| Source automatic | 345 | 5,246 | 16,281 |
| Source focused | 2 | 68 | 16,281 |
| Restored automatic | 8 | 65 | 0 |
| Restored focused | 2 | 77 | 0 |

Every view reports depth truncation; none contains a cycle omission. Source diagnostics comprise 16,260 unresolved calls, nineteen inferred test-project classifications, and two duplicate-member reports. Missing OpenIddict and MVC symbols prevent source mode from binding several controller calls, including the extracted helper at its call sites. Automatic discovery still includes that helper's new body. Restored analysis selects `src/OrchardCore.Modules/OrchardCore.OpenId/OrchardCore.OpenId.csproj` at `net10.0`, binds the helper and identifier extensions, and reports no diagnostics. The historical SDK request is `10.0.302` with `latestMajor` roll-forward.

The source automatic result has 514 modified nodes, all explicitly omitted below the depth bound, plus nineteen added and sixteen removed nodes. The additions and removals match the changed logout guards and extracted helper. The larger affected-root set was reviewed separately. All 345 roots have a projected path to a changed method: 340 to the server-settings recipe handler, one to the deployment source, and four to controller or identifier changes. A representative path runs from a content helper through content-version handlers, workflow dispatch, tenant setup, recipe execution, and `OpenIdServerSettingsStep.HandleAsync`. The selected paths contain 634 distinct edges across 218 source files; 217 files are byte-identical across the pins. The only changed intermediate file is the reviewed access controller. Workflow configuration, recipe step names, active features, and DI registrations can prevent these possible paths from executing. The graph does not prove that every root runs this recipe.

The restored automatic roots are authorization, authorization acceptance, logout, logout acceptance, token exchange, user information, the deployment-source adapter, and the recipe-step adapter. Their depth omissions lead to the changed identifier or handler bodies. Focused output exposes the actual extraction and guard changes. The earlier inline sign-out calls are removed at their old locations and added beneath the new helper; no cross-method move annotation is claimed.

Text and Markdown agree after removing fences. The review classifies every added, removed, and modified automatic node, checks the focused call structure against source, and preserves the explicit source binding failures. The shared audit checks 13,826 node locations and 32,562 diagnostic locations for these four views against immutable Git blobs. No generated definition occurs in these views. Root-path evidence is recorded in `artifacts/orchardcore-openid-automatic-root-review.json`; format and location evidence is in the corresponding expansion reports.

The existing [OrchardCore coverage issue](../issues/orchardcore-coverage.md) remains applicable. These snapshots describe possible calls and branch syntax. They do not establish protocol security, runtime authentication outcomes, or deployed feature configuration.
