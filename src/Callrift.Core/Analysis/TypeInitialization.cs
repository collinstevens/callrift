using Microsoft.CodeAnalysis;

namespace Callrift.Core;

internal static class TypeInitialization
{
    public static bool Triggers(IMethodSymbol method) => method.MethodKind == MethodKind.Ordinary && (method.IsStatic || method.ContainingType.IsValueType)
        || method.MethodKind == MethodKind.Constructor && !(method.IsImplicitlyDeclared && method.ContainingType.IsValueType);

    public static CallGraph Bind(CallGraph graph, CancellationToken cancellationToken)
    {
        var initializers = graph.Members.Values.Where(member => member.TypeInitializerType is not null)
            .GroupBy(member => member.TypeInitializerType!.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(member => member.Key, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var diagnostics = graph.Diagnostics.ToList();
        CallStep? Trigger(DispatchType? type, bool field, SourceLocation location, string? scope)
        {
            if (type is null || !initializers.TryGetValue(type.Name, out var candidates)) return null;
            var scoped = scope is null ? candidates : candidates.Where(member => member.Key.StartsWith(scope + "::", StringComparison.Ordinal)).ToArray();
            if (scoped.Length == 0) return null;
            if (scope is null || scoped.Length != 1)
            {
                var keys = scoped.Select(member => member.Key).ToArray();
                var label = scoped[0].Label;
                diagnostics.Add(new AnalysisDiagnostic("unresolved-static-initializer", $"Cannot bind a unique declaring scope for {label}; expansion omitted.", location));
                return new CallStep("unresolved", "?initialize:" + type.Name, "? " + label, false, location, []) { Candidates = keys };
            }
            var target = scoped[0];
            if (target.BeforeFieldInit && !field) return null;
            var bindings = new Dictionary<string, DispatchType>(StringComparer.Ordinal);
            DispatchTypeCatalog.Bind(target.TypeInitializerType!, type, bindings);
            var call = new CallStep("call", target.Key, target.Label, true, location, [])
            { SuppressDispatch = true, GenericArguments = bindings };
            return new CallStep("branch", "branch:initialization:" + target.Key, "possible " + target.Label, true, location, [call])
            { IsInitialization = true };
        }
        IReadOnlyList<CallStep> Calls(IEnumerable<CallStep> calls)
        {
            var result = new List<CallStep>();
            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Trigger(call.InitializationTriggerType, call.InitializationTriggerIsField, call.Location, call.InitializationScope) is { } trigger)
                    result.Add(trigger with { Relation = call.Relation, CallbackGroup = call.CallbackGroup });
                if (call.Kind != "initialize") result.Add(call with { Children = Calls(call.Children) });
            }
            return result;
        }
        var members = graph.Members.ToDictionary(pair => pair.Key, pair =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var member = pair.Value;
            var calls = Calls(member.Calls);
            if (Trigger(member.InitializationTriggerType, false, member.Location, member.InitializationScope) is { } trigger) calls = new[] { trigger }.Concat(calls).ToArray();
            return member with { Calls = calls };
        }, StringComparer.Ordinal);
        return graph with
        {
            Members = members,
            Diagnostics = diagnostics.Distinct().OrderBy(diagnostic => diagnostic.Location?.Path, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Location?.Line)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal).ToArray()
        };
    }
}
