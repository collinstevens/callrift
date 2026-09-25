using System.Text;

namespace Callrift.Core;

public static class DiffRenderer
{
    public static string Render(DiffResult result, DiffOptions options, bool markdown = false)
    {
        var output = new StringBuilder();
        var expanded = new Dictionary<string, List<DiffNode>>(StringComparer.Ordinal);
        var showAll = result.Command != "diff";
        foreach (var root in result.Trees.Where(t => showAll || t.HasChanges))
        {
            if (output.Length > 0) output.Append('\n');
            Write(root, "", "", output, expanded, options, showAll);
        }
        if (output.Length == 0) output.Append(result.Command == "diff" ? "No call-flow changes.\n" : "No call paths found.\n");
        if (result.Truncated && result.Command == "reach") output.Append("Search truncated; additional paths may exist.\n");
        if (!markdown) return output.ToString();
        var fence = "```";
        while (output.ToString().Contains(fence, StringComparison.Ordinal)) fence += "`";
        return fence + "diff\n" + output + fence + "\n";
    }

    private static void Write(DiffNode node, string indent, string connector, StringBuilder output, Dictionary<string, List<DiffNode>> expanded, DiffOptions options, bool showAll)
    {
        var repeated = !showAll && node.HasChanges && node.Children.Count > 0 && !node.Key.StartsWith("branch:", StringComparison.Ordinal) && WasExpanded(node, expanded, options.Locations);
        output.Append(node.Mark).Append(' ').Append(indent).Append(connector).Append(node.Label);
        if (node.Detail is not null) output.Append(" (").Append(node.Detail).Append(')');
        if (options.Locations)
        {
            var location = Location(node);
            if (location is not null) output.Append(" [").Append(location.Path).Append(':').Append(location.Line).Append(']');
        }
        if (repeated) output.Append(" ↑ as above");
        output.Append('\n');
        if (repeated || !showAll && !node.HasChanges) return;
        var visible = showAll ? node.Children : Trim(node.Children, options.Context);
        var merged = new List<(DiffNode Node, int Count)>();
        foreach (var child in visible)
        {
            if (merged.Count > 0 && Equivalent(merged[^1].Node, child, options.Locations))
                merged[^1] = (merged[^1].Node, merged[^1].Count + 1);
            else merged.Add((child, 1));
        }
        var nextIndent = indent + (connector.Length == 0 ? "" : connector.StartsWith('└') ? "   " : "│  ");
        for (var i = 0; i < merged.Count; i++)
        {
            var (child, count) = merged[i];
            Write(count == 1 ? child : child with { Label = child.Label + " ×" + count }, nextIndent,
                i == merged.Count - 1 ? "└─ " : "├─ ", output, expanded, options, showAll);
        }
    }

    private static bool WasExpanded(DiffNode node, Dictionary<string, List<DiffNode>> expanded, bool locations)
    {
        var key = node.Mark + node.Key;
        if (!expanded.TryGetValue(key, out var previous)) expanded[key] = previous = [];
        if (previous.Any(p => Equivalent(p, node, locations))) return true;
        previous.Add(node);
        return false;
    }

    private static IReadOnlyList<DiffNode> Trim(IReadOnlyList<DiffNode> children, int context)
    {
        if (context < 0 || children.All(c => c.HasChanges)) return children;
        if (!children.Any(c => c.HasChanges))
            return children.Count <= context ? children : children.Take(context).Append(new DiffNode("ellipsis", "…", ' ', [])).ToArray();
        var keep = new bool[children.Count];
        for (var i = 0; i < children.Count; i++)
            if (children[i].HasChanges)
                for (var j = Math.Max(0, i - context); j <= Math.Min(children.Count - 1, i + context); j++) keep[j] = true;
        var visible = new List<DiffNode>();
        for (var i = 0; i < children.Count; i++)
        {
            if (keep[i]) visible.Add(children[i]);
            else if (i == 0 || keep[i - 1]) visible.Add(new DiffNode("ellipsis", "…", ' ', []));
        }
        return visible;
    }

    private static SourceLocation? Location(DiffNode node)
    {
        var side = node.After ?? node.Before;
        return side?.CallSites.FirstOrDefault() ?? side?.Definition;
    }

    private static bool Equivalent(DiffNode left, DiffNode right, bool locations) => left.Key == right.Key && left.Mark == right.Mark && left.Label == right.Label
        && left.Detail == right.Detail && (!locations || Location(left) == Location(right))
        && left.Children.Count == right.Children.Count && left.Children.Zip(right.Children).All(p => Equivalent(p.First, p.Second, locations));
}
