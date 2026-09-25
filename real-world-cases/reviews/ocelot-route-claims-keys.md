# Ocelot URL-shaped route claims

Status: accepted after source review and independent harness comparison.

Pinned pair: `8646f482fc8c4aead02b6769762b6eeef73e9e6d` to `d1f22d930430410bfd0233f5e9e4f92980cf7df0` in [Ocelot](https://github.com/ThreeMammals/Ocelot). Both revisions contain the inspected MIT LICENSE.md blob `7a0788f9c32c526f1de1cf2e8ba0ae5a260c6c62`. Repository source and restore assets remain in the external cache.

The new RouteClaimsRequirementPostConfigureOptions recursively reads configuration sections and recombines colon-separated keys. PostConfigure retains the null-options guard, iterates routes, and assigns only nonempty requirements. OcelotBuilder moves ASP.NET service setup before configuration registration and moves the authorizers into Features.AddOcelotAuthorization. That extension registers the new post-configurer instance.

Automatic restored discovery contains the new PostConfigure method and four service-registration overloads. Source discovery contains PostConfigure, three registration overloads, and five testing-support wrapper roots. The missing fourth registration root has callers in that broader source scope. AuthenticationSteps, DiscoverySteps, and WebSocketsSteps call AddOcelot through directly read wrappers and callbacks. The separately packaged Ocelot.Testing project declares neither IsTestProject nor a test framework; its directory name does not exclude it.

Focused views show moved builder setup, the default-builder method group, removed direct authorizer registrations, and the added extension. Recursive configuration reading reaches the depth bound. Framework invocation of PostConfigure is not inferred from DI registration; its standalone root records that coverage boundary.

Both modes use automatic depth-one discovery and a focused depth-three view with external calls. Restored views select the core project at net10.0. Historical global.json files contain only a test-runner selector or are absent; these pairs do not pin an SDK. Source mode covers C# outside excluded test projects with BCL references; restored mode uses the selected project and its references. Framework conventions and implicit members are tracked in [the coverage issue](../issues/ocelot-coverage.md).

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots | 9 | 34 | 1116 | yes |
| source-focused | 3 | 142 | 1116 | yes |
| msbuild-roots | 5 | 15 | 0 | yes |
| msbuild-focused | 3 | 142 | 0 | yes |

All twenty trial views completed in JSON, text, and Markdown. Text and Markdown agree after removing fences. The shared audit checked 3,222 source-node locations and 10,884 diagnostic locations against 1,203 immutable revision/path inputs. Two generated definitions in the JSON-merge pair were checked against independent external rebuilds with emitted regex-generator source. Node IDs are unique within each view, and paths are relative. All restored views have zero diagnostics; source diagnostics are unresolved-call reports. Independent harness regeneration matched all twenty views in text, Markdown, and every JSON field. Forty snapshots are accepted across these five pairs. All twenty Ocelot views passed against the cancellation implementation in the 28-view addition replay. The 51 existing corpus checks passed separately against the same build. Cross-platform CI repeat remains pending.
