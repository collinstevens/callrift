# Ocelot custom JSON configuration merging

Status: accepted after source review and independent harness comparison.

Pinned pair: `c5ee51014905910587a75ac36ee80434664006df` to `7b38a95066e75c4ec2fb434ca91c3584282319c1` in [Ocelot](https://github.com/ThreeMammals/Ocelot). Both revisions contain the inspected MIT LICENSE.md blob `7a0788f9c32c526f1de1cf2e8ba0ae5a260c6c62`. Repository source and restore assets remain in the external cache.

This feature changes the core library and the Configuration sample. GetMergedOcelotJson becomes a builder extension, guards the schema, reads JToken values, caches merged configuration, and calls OcelotMergeConfiguration. The helper conditionally merges GlobalConfiguration, then Aggregates, Routes, and DynamicRoutes. Object sections are replaced; array sections are merged. New query helpers preserve case-insensitive property search, callbacks, conditional access, and route/global fallback order. Serialization gains a JObject overload and a shared file helper; AddOcelotBaseUrl is removed.

Controller actions change IActionResult to ActionResult and format caught exceptions through ToShortString/GetMessages. Error adds an exception null guard; UnknownError gains constructors and status 500. The validator-registration helper is renamed together with its caller. Restored automatic roots include controller actions, configuration entry helpers, registration methods, UnknownError constructors, requester middleware, request-ID middleware, exception middleware, and tracing. The last three reach Error through IRequestScopedDataRepository.Add and HttpDataRepository's CannotAddDataError catch path. Source roots additionally expose the expanded Configuration sample and testing-support callers.

Focused views retain file selection and the primary/global-file guards around the new merge helper. They show the ordered section merges and controller catch path. The unique static OcelotMergeConfiguration target remains Roslyn-bound with dynamic operands; a separate Roslyn inspection confirmed this selected symbol despite a DynamicInvocation operation. This does not prove runtime dynamic argument conversion. Missing package bindings are explicit in source mode.

Both modes use automatic depth-one discovery and a focused depth-three view with external calls. Restored views select the core project at net10.0. Historical global.json files contain only a test-runner selector or are absent; these pairs do not pin an SDK. Source mode covers C# outside excluded test projects with BCL references; restored mode uses the selected project and its references. Framework conventions and implicit members are tracked in [the coverage issue](../issues/ocelot-coverage.md).

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots | 32 | 208 | 1130 | yes |
| source-focused | 3 | 136 | 1130 | yes |
| msbuild-roots | 29 | 98 | 0 | yes |
| msbuild-focused | 3 | 136 | 0 | yes |

All twenty trial views completed in JSON, text, and Markdown. Text and Markdown agree after removing fences. The shared audit checked 3,222 source-node locations and 10,884 diagnostic locations against 1,203 immutable revision/path inputs. Two generated definitions in the JSON-merge pair were checked against independent external rebuilds with emitted regex-generator source. Node IDs are unique within each view, and paths are relative. All restored views have zero diagnostics; source diagnostics are unresolved-call reports. Independent harness regeneration matched all twenty views in text, Markdown, and every JSON field. Forty snapshots are accepted across these five pairs. All twenty Ocelot views passed against the cancellation implementation in the 28-view addition replay. The 51 existing corpus checks passed separately against the same build. Cross-platform CI repeat remains pending.
