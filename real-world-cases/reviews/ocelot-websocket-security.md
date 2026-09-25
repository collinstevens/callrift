# Ocelot WebSocket security

Status: accepted after source review and independent harness comparison.

Pinned pair: `6062d5a5ab3bc1baa929d0965e5a28e3cbf2d1fd` to `f156fd4017ca25025fffdad8ec56c1d657dfb402` in [Ocelot](https://github.com/ThreeMammals/Ocelot). Both revisions contain the inspected MIT LICENSE.md blob `058bbbcec4d6137a36c5cc6a46a599f93bb10c42`. Repository source and restore assets remain in the external cache.

ConfigureWebSockets adds SecurityMiddleware after multiplexing. IPSecurityPolicy flattens blocked/allowed-list guards and creates SecurityError instead of UnauthenticatedError. SecurityError forwards its code/status to Error. SecurityMiddleware adds constructor argument checks, loops through policies, and places UpsertErrors and HandleWebSocketErrors inside the error branch. The next delegate remains after the loop. HandleWebSocketErrors assigns the response status under IsWebSocketRequest. The status mapper adds SecurityError to the forbidden predicate.

Restored automatic roots include the two outer UseOcelot overloads, responder middleware, SecurityAsync, the new SecurityError constructor, and the changed SecurityMiddleware constructor/Invoke. The UseOcelot wrappers reach BuildOcelotPipeline and its ConfigureWebSockets callback. Source mode cannot bind that framework chain and instead exposes ConfigureWebSockets directly; its additional Metadata sample root reaches the status mapper through a responder callback.

Focused views show the added middleware registration, Task.Run callback, policy branch changes, and error-handling relocation. The helper's property execution and framework-driven middleware invocation remain outside the graph. Registration order is visible syntax, not proof of runtime pipeline behavior.

Both modes use automatic depth-one discovery and a focused depth-three view with external calls. Restored views select the core project at net10.0. Historical global.json files contain only a test-runner selector or are absent; these pairs do not pin an SDK. Source mode covers C# outside excluded test projects with BCL references; restored mode uses the selected project and its references. Framework conventions and implicit members are tracked in [the coverage issue](../issues/ocelot-coverage.md).

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots | 6 | 48 | 1031 | yes |
| source-focused | 3 | 76 | 1031 | yes |
| msbuild-roots | 7 | 37 | 0 | yes |
| msbuild-focused | 3 | 77 | 0 | yes |

All twenty trial views completed in JSON, text, and Markdown. Text and Markdown agree after removing fences. The shared audit checked 3,222 source-node locations and 10,884 diagnostic locations against 1,203 immutable revision/path inputs. Two generated definitions in the JSON-merge pair were checked against independent external rebuilds with emitted regex-generator source. Node IDs are unique within each view, and paths are relative. All restored views have zero diagnostics; source diagnostics are unresolved-call reports. Independent harness regeneration matched all twenty views in text, Markdown, and every JSON field. Forty snapshots are accepted across these five pairs. All twenty Ocelot views passed against the cancellation implementation in the 28-view addition replay. The 51 existing corpus checks passed separately against the same build. Cross-platform CI repeat remains pending.
