# Ocelot downstream timeout status

Status: accepted after source review and independent harness comparison.

Pinned pair: `65686d488475163cc357b7f5876e3d346e23630e` to `8646f482fc8c4aead02b6769762b6eeef73e9e6d` in [Ocelot](https://github.com/ThreeMammals/Ocelot). Both revisions contain the inspected MIT LICENSE.md blob `7a0788f9c32c526f1de1cf2e8ba0ae5a260c6c62`. Repository source and restore assets remain in the external cache.

RequestTimedOutError changes to a primary constructor and uses status 504. HttpExceptionToErrorMapper narrows its TimeoutException condition to exceptions with no inner exception. Custom mappers retain precedence. Exceptions with inner exceptions continue to later connection/payload handling. ErrorsToHttpStatusCodeMapper changes the timeout status while preserving the surrounding authorization, quota, cancellation, and route predicates.

Focused views show the changed guard and constructor calls. The status mapper is marked body-changed with visible calls unchanged; the call graph does not display constant return values. Source binding cannot resolve the ASP.NET status constant in the base constructor and records that unresolved call. Restored binding resolves it.

Automatic restored discovery contains requester and responder middleware. Source discovery contains requester middleware plus the Metadata sample entry through its responder callback. The sample is an application, not a test. The reviewed call chain is requester Invoke, possible MessageInvokerHttpRequester.GetResponse, exception mapper, and RequestTimedOutError; the responder chain reaches its status mapper.

Both modes use automatic depth-one discovery and a focused depth-three view with external calls. Restored views select the core project at net10.0. Historical global.json files contain only a test-runner selector or are absent; these pairs do not pin an SDK. Source mode covers C# outside excluded test projects with BCL references; restored mode uses the selected project and its references. Framework conventions and implicit members are tracked in [the coverage issue](../issues/ocelot-coverage.md).

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots | 2 | 27 | 1104 | yes |
| source-focused | 2 | 42 | 1104 | yes |
| msbuild-roots | 2 | 21 | 0 | yes |
| msbuild-focused | 2 | 42 | 0 | yes |

All twenty trial views completed in JSON, text, and Markdown. Text and Markdown agree after removing fences. The shared audit checked 3,222 source-node locations and 10,884 diagnostic locations against 1,203 immutable revision/path inputs. Two generated definitions in the JSON-merge pair were checked against independent external rebuilds with emitted regex-generator source. Node IDs are unique within each view, and paths are relative. All restored views have zero diagnostics; source diagnostics are unresolved-call reports. Independent harness regeneration matched all twenty views in text, Markdown, and every JSON field. Forty snapshots are accepted across these five pairs. All twenty Ocelot views passed against the cancellation implementation in the 28-view addition replay. The 51 existing corpus checks passed separately against the same build. Cross-platform CI repeat remains pending.
