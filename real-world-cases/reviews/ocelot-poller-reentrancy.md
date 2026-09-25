# Ocelot polling reentrancy

Status: accepted after source review and independent harness comparison.

Pinned pair: `e4022a7d80d2e05f020fc8c1fb50f5dde72256d4` to `c20325bcb6a4d080ed1172d5303811031e532b03` in [Ocelot](https://github.com/ThreeMammals/Ocelot). Both revisions contain the inspected MIT LICENSE.md blob `058bbbcec4d6137a36c5cc6a46a599f93bb10c42`. Repository source and restore assets remain in the external cache.

TryEnterPolling uses Interlocked.CompareExchange before both Poll and PollAsync. Each body now has an outer finally that calls ExitPolling/Volatile.Write after normal completion, null configuration, or repository exceptions. The existing synchronous Get/Create/GetResult path and asynchronous GetAsync/Create path remain. OnTimer loses its former flag/try/finally wrapper and delegates to Poll. StartAsync stores the period; StopAsync and Dispose atomically exchange the timer before stopping or disposing it.

Restored automatic discovery contains Dispose, PollAsync, StartAsync, and StopAsync. Source mode replaces the standalone Dispose root with WatchKube.Dispose because its IDisposable subscription has possible targets across the broader source compilation. This is conservative type-level dispatch, not evidence that the subscription is a FileConfigurationPoller. The focused five-entry view independently includes Dispose and OnTimer along with the public lifecycle/polling methods.

Focused views show CompareExchange, moved repository/creator calls, outer finally/Volatile.Write, the timer callback, and Exchange in stop/disposal. Structural movement changes expansion depth and can replace expanded bodies with explicit depth omissions. The graph does not establish atomicity, thread scheduling, or reentrancy safety.

Both modes use automatic depth-one discovery and a focused depth-three view with external calls. Restored views select the core project at net10.0. Historical global.json files contain only a test-runner selector or are absent; these pairs do not pin an SDK. Source mode covers C# outside excluded test projects with BCL references; restored mode uses the selected project and its references. Framework conventions and implicit members are tracked in [the coverage issue](../issues/ocelot-coverage.md).

| View | Roots | Nodes | Diagnostics | Truncated |
|---|---:|---:|---:|---|
| source-roots | 4 | 57 | 1061 | yes |
| source-focused | 5 | 132 | 1061 | yes |
| msbuild-roots | 4 | 40 | 0 | yes |
| msbuild-focused | 5 | 132 | 0 | yes |

All twenty trial views completed in JSON, text, and Markdown. Text and Markdown agree after removing fences. The shared audit checked 3,222 source-node locations and 10,884 diagnostic locations against 1,203 immutable revision/path inputs. Two generated definitions in the JSON-merge pair were checked against independent external rebuilds with emitted regex-generator source. Node IDs are unique within each view, and paths are relative. All restored views have zero diagnostics; source diagnostics are unresolved-call reports. Independent harness regeneration matched all twenty views in text, Markdown, and every JSON field. Forty snapshots are accepted across these five pairs. All twenty Ocelot views passed against the cancellation implementation in the 28-view addition replay. The 51 existing corpus checks passed separately against the same build. Cross-platform CI repeat remains pending.
