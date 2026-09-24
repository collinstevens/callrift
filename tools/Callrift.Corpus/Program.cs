using System.Text.Json;
using Callrift.Core;
using Callrift.Corpus;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: corpus prepare | candidates REPOSITORY [LIMIT]");
    return 2;
}
if (args[0] == "prepare")
{
    foreach (var entry in CorpusStore.ReadManifest())
        Console.WriteLine(await CorpusStore.PrepareAsync(entry));
    return 0;
}
if (args[0] != "candidates" || args.Length < 2)
    return 2;
var repo = await GitRepository.OpenAsync(args[1]);
var limit = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 10;
var history = await GitRepository.RunAsync(repo.Root, ["log", "-300", "--no-merges", "--format=%H %s", "--", "*.cs"]);
var candidates = new List<object>();
foreach (var line in history.Split('\n', StringSplitOptions.RemoveEmptyEntries))
{
    var sha = line[..40];
    var files = (await GitRepository.RunAsync(repo.Root, ["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", sha, "--", "*.cs"]))
        .Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(p => !p.Split('/').Any(s => s.Equals("tests", StringComparison.OrdinalIgnoreCase) || s.Equals("test", StringComparison.OrdinalIgnoreCase))).ToArray();
    if (files.Length is < 1 or > 30) continue;
    var parent = (await GitRepository.RunAsync(repo.Root, ["rev-parse", sha + "^"])).Trim();
    var bodyChanged = false;
    foreach (var file in files)
    {
        var bodies = new List<string>();
        foreach (var revision in new[] { parent, sha })
        {
            string source;
            try { source = await GitRepository.RunAsync(repo.Root, ["show", revision + ":" + file]); }
            catch (InvalidOperationException) { source = ""; }
            var tree = CSharpSyntaxTree.ParseText(source);
            bodies.Add(string.Join("|", tree.GetRoot().DescendantNodes().OfType<BaseMethodDeclarationSyntax>().Select(m =>
                string.Join(" ", ((Microsoft.CodeAnalysis.SyntaxNode?)m.Body ?? m.ExpressionBody)?.DescendantTokens().Select(t => t.Text) ?? []))));
        }
        if (bodies[0] != bodies[1]) { bodyChanged = true; break; }
    }
    if (!bodyChanged) continue;
    candidates.Add(new { before = parent, after = sha, subject = line[41..], files });
    if (candidates.Count >= limit) break;
}
Console.WriteLine(JsonSerializer.Serialize(candidates, new JsonSerializerOptions { WriteIndented = true }));
return 0;
