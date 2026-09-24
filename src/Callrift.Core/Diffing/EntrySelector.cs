namespace Callrift.Core;

public static class EntrySelector
{
    public static IReadOnlyList<string> Select(CallGraph before, CallGraph after, IReadOnlySet<string> changed, DiffOptions options)
    {
        var members = before.Members.Concat(after.Members).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        if (options.Entries.Count > 0 || options.Files.Count > 0)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in options.Entries)
            {
                var matches = members.Values.Where(m => m.Key == entry).ToArray();
                if (matches.Length == 0)
                    matches = members.Values.Where(m => m.Label == entry).ToArray();
                if (matches.Length == 0)
                    matches = members.Values.Where(m => m.Label.EndsWith("." + entry, StringComparison.Ordinal) || m.Key.EndsWith(entry, StringComparison.Ordinal)).ToArray();
                if (matches.Select(m => m.MatchName).Distinct().Count() == 1 && matches.Length <= 2
                    && matches.Count(m => before.Members.ContainsKey(m.Key)) == 1 && matches.Count(m => after.Members.ContainsKey(m.Key)) == 1)
                    selected.UnionWith(matches.Select(m => m.Key));
                else if (matches.Length == 1)
                    selected.Add(matches[0].Key);
                else
                    throw new InvalidOperationException(matches.Length == 0 ? $"Entry not found: {entry}" : $"Ambiguous entry '{entry}': {string.Join(", ", matches.Select(m => m.Key).Order(StringComparer.Ordinal))}");
            }
            foreach (var file in options.Files)
            {
                var paths = members.Values.Select(m => m.Location.Path).Distinct(StringComparer.Ordinal).ToArray();
                var normalized = file.Replace('\\', '/');
                var matching = paths.Where(p => p == normalized).ToArray();
                if (matching.Length == 0)
                    matching = paths.Where(p => p.EndsWith("/" + normalized, StringComparison.Ordinal)).ToArray();
                if (matching.Length != 1)
                    throw new InvalidOperationException(matching.Length == 0 ? $"File not found: {file}" : $"Ambiguous file: {file}");
                selected.UnionWith(members.Values.Where(m => m.Location.Path == matching[0]).Select(m => m.Key));
            }
            return selected.Order(StringComparer.Ordinal).ToArray();
        }
        var callers = members.Keys.ToDictionary(k => k, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var graph in new[] { before, after })
        {
            foreach (var member in graph.Members.Values)
                foreach (var target in Targets(member.Calls))
                    if (callers.TryGetValue(target, out var incoming))
                        incoming.Add(member.Key);
        }
        foreach (var graph in new[] { before, after })
        {
            foreach (var dispatch in graph.Implementations.Where(p => callers.TryGetValue(p.Key, out var parents) && parents.Count > 0))
                foreach (var implementation in dispatch.Value)
                    if (callers.TryGetValue(implementation, out var incoming))
                    {
                        if (!callers.ContainsKey(dispatch.Key)) callers[dispatch.Key] = [];
                        incoming.Add(dispatch.Key);
                    }
        }
        var affected = new HashSet<string>(changed.Where(key => options.Paths.Count == 0 ||
            options.Paths.Any(p => members.TryGetValue(key, out var member) && (member.Location.Path == p || member.Location.Path.StartsWith(p.TrimEnd('/') + "/", StringComparison.Ordinal)))), StringComparer.Ordinal);
        var pending = new Queue<string>(affected);
        while (pending.TryDequeue(out var key))
            if (callers.TryGetValue(key, out var incoming))
                foreach (var caller in incoming)
                    if (affected.Add(caller)) pending.Enqueue(caller);
        var components = StrongComponents(affected, callers);
        var ownership = components.SelectMany((c, i) => c.Select(k => (Key: k, Component: i))).ToDictionary(p => p.Key, p => p.Component);
        return components.Where(c => !c.Any(k => callers.TryGetValue(k, out var incoming) && incoming.Any(p => ownership.TryGetValue(p, out var owner) && owner != ownership[k])))
            .Select(c => c.Where(k => members.TryGetValue(k, out var member) && member.HasBody).Order(StringComparer.Ordinal).FirstOrDefault())
            .Where(k => k is not null).Cast<string>().Order(StringComparer.Ordinal).ToArray();
    }

    internal static IEnumerable<string> Targets(IEnumerable<CallStep> calls)
    {
        foreach (var call in calls)
        {
            if (call.Kind == "call" && call.IsSource) yield return call.Key;
            foreach (var child in Targets(call.Children)) yield return child;
        }
    }

    private static List<List<string>> StrongComponents(HashSet<string> nodes, Dictionary<string, HashSet<string>> edges)
    {
        var index = 0;
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<List<string>>();
        void Visit(string key)
        {
            indexes[key] = low[key] = index++;
            stack.Push(key);
            active.Add(key);
            if (edges.TryGetValue(key, out var adjacent))
                foreach (var next in adjacent.Where(nodes.Contains).Order(StringComparer.Ordinal))
                {
                    if (!indexes.ContainsKey(next)) { Visit(next); low[key] = Math.Min(low[key], low[next]); }
                    else if (active.Contains(next)) low[key] = Math.Min(low[key], indexes[next]);
                }
            if (low[key] != indexes[key]) return;
            var component = new List<string>();
            string popped;
            do { popped = stack.Pop(); active.Remove(popped); component.Add(popped); } while (popped != key);
            components.Add(component);
        }
        foreach (var key in nodes.Order(StringComparer.Ordinal))
            if (!indexes.ContainsKey(key)) Visit(key);
        return components;
    }
}
