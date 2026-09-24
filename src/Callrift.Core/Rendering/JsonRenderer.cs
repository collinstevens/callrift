using System.Text.Encodings.Web;
using System.Text.Json;

namespace Callrift.Core;

public static class JsonRenderer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 1024,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Render(DiffResult result)
    {
        var nextId = 0;
        object Node(DiffNode node, Dictionary<string, string> ancestors)
        {
            var id = "n" + nextId++;
            var path = new Dictionary<string, string>(ancestors, StringComparer.Ordinal);
            path[node.Key] = id;
            foreach (var side in new[] { node.Before, node.After })
            {
                if (side?.SymbolId is { } key) path[key] = id;
                if (side is { TargetIds.Count: 1 }) path[side.TargetIds[0]] = id;
            }
            var reference = node.Omission?.SymbolId is { } symbol && ancestors.TryGetValue(symbol, out var referenceId) ? referenceId : null;
            return new
            {
                id,
                node.Kind,
                change = node.Mark switch { '+' => "added", '-' => "removed", '~' => "modified", _ => "unchanged" },
                node.Label,
                node.Detail,
                before = node.Before,
                after = node.After,
                children = node.Children.Select(child => Node(child, path)).ToArray(),
                omission = node.Omission is null ? null : new { node.Omission.Reason, node.Omission.SymbolId, referenceId = reference }
            };
        }
        var nodes = result.Trees.Select(node => Node(node, new Dictionary<string, string>(StringComparer.Ordinal))).ToArray();
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            result.Command,
            result.From,
            result.To,
            analysis = result.Coverage,
            result.HasChanges,
            result.Truncated,
            result.Diagnostics,
            trees = result.Command == "reach" ? [] : nodes,
            paths = result.Command == "reach" ? nodes : []
        }, Options).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}
