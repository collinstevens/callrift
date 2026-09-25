# Generic caller substitutions

Status: open in source and MSBuild modes.

The graph indexes a generic member by its definition. A call to `Router<int>.Run` currently expands the open `Router<T>.Run` body without substituting `int` for `T`. Its nested `IHandler<T>.Run` dispatch can consequently include an implementation for `string`.

The independent CLI reproduction uses these declarations:

```csharp
public interface IHandler<T> { void Run(T value); }
public sealed class TextHandler : IHandler<string> { public void Run(string value) => Sink.Before(); }
public sealed class NumberHandler : IHandler<int> { public void Run(int value) => Sink.Number(); }
public sealed class Router<T> {
    private readonly IHandler<T> handler;
    public Router(IHandler<T> handler) { this.handler = handler; }
    public void Run(T value) => handler.Run(value);
}
public static class Entry {
    public static void Text(Router<string> router) => router.Run("text");
    public static void Number(Router<int> router) => router.Run(1);
}
public static class Sink { public static void Before() {} public static void After() {} public static void Number() {} }
```

Changing only `TextHandler.Run` from `Sink.Before()` to `Sink.After()` incorrectly marks both `Entry.Text` and `Entry.Number` affected. The tree for `Entry.Number` includes `TextHandler.Run` and `Sink.After`. Both modes reproduce this with zero diagnostics. `NumberHandler.Run` and its caller are unchanged and cannot follow that string-handler path.

The OrchardCore media review exposed the need for this audit. At `22e6852c825c26f727a47a0388b46629dc99ae81`, `NodeController` injects `IDisplayManager<MenuItem>`, while `DisplayManager<TModel>` invokes `IDisplayDriver<TModel>` callbacks. The broad source-only affected-controller set is still under review; that historical pair has not been accepted.

Checking a directly bound contract's variance does not propagate caller substitutions into another member. Resolution must carry generic type and method arguments through direct calls, implementation selection, callbacks, root discovery, tree expansion, depth omissions, and reach queries. Recursive generic instantiations need bounded, explicit handling. Unbound parameters and generic constraints also need a separate audit. Until then, these graphs remain partial and can contain infeasible paths.
