# Generic caller substitutions

Status: type and method invocation arguments are preserved in source and MSBuild modes. Open parameter relationships, receiver flow, and bounded expansion remain explicit limitations.

The original graph indexed a generic member only by its definition. A call to `Router<int>.Run` expanded the open `Router<T>.Run` body without substituting `int` for `T`. Its nested `IHandler<T>.Run` dispatch consequently included an implementation for `string`.

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

Changing only `TextHandler.Run` from `Sink.Before()` to `Sink.After()` originally marked both `Entry.Text` and `Entry.Number` affected. The tree for `Entry.Number` included `TextHandler.Run` and `Sink.After`. Both modes reproduced this with zero diagnostics. The implementation now retains the `int` argument: only `Entry.Text` is affected, and integer tree/reach queries exclude the string handler.

The OrchardCore media review exposed the need for this audit. At `22e6852c825c26f727a47a0388b46629dc99ae81`, `NodeController` injects `IDisplayManager<MenuItem>`, while `DisplayManager<TModel>` invokes `IDisplayDriver<TModel>` callbacks. The broad source-only affected-controller set is still under review; that historical pair has not been accepted.

Checking a directly bound contract's variance does not propagate caller substitutions into another member. Invocation contexts now carry type and method arguments through direct calls, implementation selection, callbacks, root discovery, tree expansion, depth omissions, and reach queries. Repeating a declaration with different arguments is not an ordinary cycle. Analysis bounds additional states at 65,536 and type shapes at 128 nodes; reaching a bound produces a diagnostic, an omission, and truncation. Unbound parameter relationships and receiver flow still need separate audits. These graphs remain partial and can contain infeasible paths.

The unaccepted ASP.NET Core CTS-disposal pair provides another concrete example. Revision `4cab91a40393e7d0341cf43cb88930588a0b4876` to `3563a8e77e0e09055a7f0f18ef9ab026dcd59ad2` adds disposal before an early return in `DefaultHubDispatcher<THub>.StreamAsync`. The original source analysis reported 1,521 affected roots and 22,734 diagnostics. A traced 43-edge path from `ApplicationModelController.GetActionSpecificDescription` to that changed method contained this infeasible segment:

```text
DefaultComplexObjectValidationStrategy.Enumerator.MoveNext
  IReadOnlyDictionary<ModelMetadata, ModelMetadata>.TryGetValue
    AdaptiveCapacityDictionary<TKey, TValue>.TryGetValue
      AdaptiveCapacityDictionary<TKey, TValue>.TryFindItem
        IEqualityComparer<TKey>.Equals
          CaseSensitiveTagHelperAttributeComparer.Equals(TagHelperAttribute, TagHelperAttribute)
```

The pinned source establishes the expected types independently. `src/Mvc/Mvc.Abstractions/src/ModelBinding/ModelMetadata.cs:163` declares the mapping as `IReadOnlyDictionary<ModelMetadata, ModelMetadata>`. `DefaultComplexObjectValidationStrategy.cs:96` calls its `TryGetValue`. In `src/Shared/Dictionary/AdaptiveCapacityDictionary.cs`, line 27 declares `_comparer` as `IEqualityComparer<TKey>`, line 489 calls `TryFindItem`, and line 582 invokes `_comparer.Equals`. `src/Shared/Razor/CaseSensitiveBoundAttributeComparer.cs:11` implements `IEqualityComparer<TagHelperAttribute>`. Preserving `TKey = ModelMetadata` excludes this comparer. An independent contextual traversal confirms the corrected binding and absence of that comparer path.

Other portions of the traced path contain conservative interface and virtual dispatch. This specific incompatible generic edge is sufficient to reject the path; it does not prove that every one of the 1,521 roots is incorrect. The pair remains outside the accepted manifest. Smaller selectors cannot establish correct automatic discovery.
