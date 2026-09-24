using System.Text;

namespace Callrift.Core;

public static class DiffRenderer
{
    public static string Render(DiffResult result, DiffOptions options, bool markdown = false)
    {
        var output = new StringBuilder();
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in result.Trees.Where(t => t.HasChanges))
        {
            if (output.Length > 0) output.Append('\n');
            Write(root, "", "", output, expanded, options);
        }
        if (output.Length == 0) output.Append("No call-flow changes.\n");
        if (!markdown) return output.ToString();
        var fence = "```";
        while (output.ToString().Contains(fence, StringComparison.Ordinal)) fence += "`";
        return fence + "diff\n" + output + fence + "\n";
    }

    private static void Write(DiffNode node, string indent, string connector, StringBuilder output, HashSet<string> expanded, DiffOptions options)
    {
        var repeated = node.HasChanges && node.Children.Count > 0 && !node.Key.StartsWith("branch:", StringComparison.Ordinal) && !expanded.Add(node.Mark + node.Key);
        output.Append(node.Mark).Append(' ').Append(indent).Append(connector).Append(node.Label);
        if (node.Detail is not null) output.Append(" (").Append(node.Detail).Append(')');
        if (repeated) output.Append(" ↑ as above");
        output.Append('\n');
        if (repeated || !node.HasChanges) return;
        var visible = Trim(node.Children, options.Context);
        var merged = new List<(DiffNode Node, int Count)>();
        foreach (var child in visible)
        {
            if (merged.Count > 0 && Equivalent(merged[^1].Node, child))
                merged[^1] = (merged[^1].Node, merged[^1].Count + 1);
            else merged.Add((child, 1));
        }
        var nextIndent = indent + (connector.Length == 0 ? "" : connector.StartsWith('└') ? "   " : "│  ");
        for (var i = 0; i < merged.Count; i++)
        {
            var (child, count) = merged[i];
            Write(count == 1 ? child : child with { Label = child.Label + " ×" + count }, nextIndent,
                i == merged.Count - 1 ? "└─ " : "├─ ", output, expanded, options);
        }
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

    private static bool Equivalent(DiffNode left, DiffNode right) => left.Key == right.Key && left.Mark == right.Mark && left.Label == right.Label
        && left.Detail == right.Detail && left.Children.Count == right.Children.Count && left.Children.Zip(right.Children).All(p => Equivalent(p.First, p.Second));
}
