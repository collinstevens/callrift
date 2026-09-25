# Receiver flow and infeasible dispatch candidates

Status: open. Applies to both source and MSBuild modes.

Virtual dispatch lists source implementations compatible with the bound method. Explicit base calls, sealed static receiver types, and directly constructed receivers use direct expansion. The graph does not propagate receiver values or narrow an object's type using enclosing guards.

The pinned `serilog-null-key` pair exposes a specific incorrect candidate set in unchanged context. At `6c3fbcf636b0671bbd6f5032b61a2254937d8408`, `src/Serilog/Formatting/Json/JsonValueFormatter.cs:269` calls `value.ToString()` inside `if (value is char)`. The static type of `value` is `object`. The JSON snapshot lists `LogEventPropertyValue`, `MessageTemplate`, `PropertyToken`, and `TextToken` overrides as possible targets. Those reference-type implementations cannot execute on that guarded path. The before revision has the same issue at line 258. Text's unchanged-context trimming hides this subtree; the JSON review exposes it.

The changed dictionary-key calls on lines 146 (before) and 154 (after) also bind to `object.ToString()`. Their source override candidates are conservative: no active Serilog conversion policy or runtime key value is inferred. Moving the call under the non-null branch is supported evidence; execution of a particular override is not.

Resolution requires flow-sensitive receiver constraints shared by expansion, affected-root discovery, reach, and JSON targets. Review guard narrowing, assignments that invalidate narrowing, joins, negated guards, and callbacks before closing this issue. Keep coverage partial and do not treat a reported potential path as proof of runtime reachability.

The Autofac held-pipeline investigation exposed a false cycle after an interface cast. At `ae9e1e1129b9c22e7ab111381308dcb02f80a8d2`, `src/Autofac/Core/Container.cs:184` calls `((IServiceProvider)_rootLifetimeScope).GetService(serviceType)`. The readonly field is declared as `LifetimeScope` on line 20 and initialized with `new LifetimeScope(ComponentRegistry)` on line 29. Dispatch previously included `Container.GetService`, creating an infeasible self-cycle, as well as `LifetimeScope.GetService`.

That cast case is fixed in both modes: static receiver constraints survive ordinary reference casts, and candidate runtime types must satisfy the receiver and contract together. Only `LifetimeScope.GetService` remains in the Autofac output. Ten CLI checks cover reference casts, sibling overrides, user-defined conversion boundaries, shared generic substitutions, and tuple types. Enclosing-guard narrowing in the Serilog example remains open.

A separate zero-diagnostic source reproduction loses the concrete `this` receiver through an inherited nonvirtual helper:

```csharp
abstract class Base { protected void Shared() => Hook(); protected abstract void Hook(); }
sealed class Left : Base { public void Entry() => Shared(); protected override void Hook() => Sink.Left(); }
sealed class Right : Base { protected override void Hook() => Sink.Right(); }
static class Sink { public static void Left() {} public static void Right() {} }
```

The reported path from `Left.Entry` through `Base.Shared` to `Right.Hook` is infeasible. `Left` is sealed, and its `this` object cannot become a sibling `Right` instance. Generic invocation arguments alone do not fix this receiver loss. Receiver constraints must survive helper expansion and possible implementation selection before this issue can close.
