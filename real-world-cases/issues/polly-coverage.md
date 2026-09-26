# Polly coverage limits

The Polly cases expose different input scopes and incomplete implicit execution. Their source and restored graphs are intentionally reviewed separately. A focused selector never replaces automatic root discovery.

Source mode reads repository C# without project defines, packages, or generated compilation inputs. It includes legacy Polly, benchmarks, samples, and the Cake build script. The pinned pairs produce roughly 2,000 explicit diagnostics, including six inferred test-project classifications. Restored views select net8.0 Polly.Core or Polly.Extensions with its Core project reference and have zero diagnostics. These clean restored compilations do not make the static graph complete.

The caller-cancellation pair selects different stack-trace code under NET. Source mode sees the reflection-based fallback; restored net8.0 sees ExceptionDispatchInfo.SetCurrentStackTrace. The executor pair similarly has an explicit disposed guard in source mode and ObjectDisposedException.ThrowIf in restored mode. These are declared mode differences, not silent fallback during a failed restore.

The telemetry-source pair adds a static Instance field and obtains its Meter through a property. Static initialization now connects TelemetryListenerImpl and its callers to the singleton's TelemetrySource constructor and GetVersion. The private constructor no longer appears as an isolated affected root. The reviewed source automatic view removes only that former root and preserves the other root identities. Property accessor execution remains incomplete; this singleton path does not establish complete coverage of Meter access or runtime initialization timing.

The secondary-action pair moves a default-generator early return into a helper and uses an early-result tuple in its caller. Return-only guards have no contained calls and are omitted under the current condition-node rules. The diff exposes body/call changes but does not infer a predicate for later calls from an earlier return. It must not be used to prove which callback executes for a particular runtime outcome.

Possible interface and virtual dispatch, callback registration, and type-level cycles remain conservative. Reload registration's IDisposable candidates do not identify the actual monitor subscription. Telemetry listener cycles do not prove runtime recursion. The completion-source overload change does not prove queue concurrency or continuation scheduling.

The normalized trial outputs preserve these limits explicitly through unresolved diagnostics, partial analysis status, depth omissions, source locations, and the adjacent per-pair reviews. All 51 real-world checks passed after local snapshot acceptance. Cross-platform execution remains pending for this case checkpoint.
