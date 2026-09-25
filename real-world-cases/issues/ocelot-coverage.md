# Ocelot coverage limits

The source views include the core library, samples, extensions, and the separately packaged Ocelot.Testing support library. That support project declares neither IsTestProject nor a test-framework package. Its callers remain in source analysis. Restored views select the core project at net10.0. The two scopes have different affected roots; focused selectors do not replace automatic discovery.

Source views report 1,031–1,130 unresolved calls per pinned pair. Missing package/project bindings affect ASP.NET middleware registration, controller methods, configuration extensions, and error constructors. Restored views have zero diagnostics. Successful binding does not make either graph a complete runtime execution model.

The route-claims pair registers IPostConfigureOptions<FileConfiguration>, but the graph does not infer framework invocation of PostConfigure. That method appears as a standalone added root. WebSocket middleware registration is similarly distinct from framework execution of Invoke. HandleWebSocketErrors changes a response property; implicit property execution remains outside the graph. The timeout pair changes status constants; the status mapper is marked body-changed even when its visible call list is unchanged.

The custom-JSON pair includes dynamic operands passed to a unique static helper. Roslyn selects that helper, so the graph retains its binding. The edge does not establish successful runtime dynamic conversion. Its two generated SubConfigRegex definition locations were checked against independent external builds with emitted generator source.

The polling pair's source-only WatchKube.Dispose root follows possible IDisposable implementations across the source compilation. The graph does not establish the concrete subscription instance. CompareExchange, Exchange, and Volatile.Write calls show syntax changes; they do not establish scheduling, atomicity, or reentrancy safety. Earlier return-only guards do not become inferred predicates on subsequent calls.

Depth omissions and partial coverage remain explicit in every view. Generic caller substitutions, enclosing-guard narrowing, implicit members, and framework conventions remain subjects of the broader correctness audit. See the separate [generic context issue](generic-context.md) and [receiver flow issue](receiver-flow.md).
