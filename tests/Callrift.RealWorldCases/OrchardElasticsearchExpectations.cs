using Callrift.Core;
using Xunit;

namespace Callrift.RealWorldCases;

internal static class OrchardElasticsearchExpectations
{
    public static void Verify(DiffResult result, bool focused)
    {
        var restored = result.Coverage.Mode == "msbuild";
        Assert.Equal("partial", result.Coverage.Status);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Trees.Count);
        if (restored) Assert.Empty(result.Diagnostics);
        else
        {
            Assert.Equal(17153, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-call"));
            Assert.Equal(19, result.Diagnostics.Count(diagnostic => diagnostic.Code == "test-project-inferred"));
            Assert.Equal(6, result.Diagnostics.Count(diagnostic => diagnostic.Code == "duplicate-member"));
            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "unresolved-static-initializer"));
        }
        foreach (var root in result.Trees)
        {
            Assert.Equal(root.Before!.SymbolId, root.After!.SymbolId);
            var added = root.Children.Where(node => node.Mark == '+').ToArray();
            var initialization = Assert.Single(added, node => node.Label == "possible initialization of ElasticsearchPermissions");
            var authorization = Assert.Single(added, node => node.Label.EndsWith("AuthorizeAsync", StringComparison.Ordinal));
            Assert.True(Array.IndexOf(added, initialization) < Array.IndexOf(added, authorization));
            var lookup = root.Children.ToList().FindIndex(node => node.Label.StartsWith("IIndexProfileStore.FindBy", StringComparison.Ordinal));
            Assert.True(lookup > root.Children.ToList().IndexOf(authorization));
            if (restored && !focused)
            {
                Assert.Equal("depth-limit", authorization.Omission!.Reason);
                Assert.DoesNotContain(root.Children, node => node.Label.StartsWith("if (!await _authorizationService", StringComparison.Ordinal));
                continue;
            }
            var guard = Assert.Single(added, node => node.Label.StartsWith("if (!await _authorizationService", StringComparison.Ordinal));
            Assert.Equal(restored ? "Forbid" : "? Forbid", Assert.Single(guard.Children).Label);
            Assert.True(root.Children.ToList().IndexOf(guard) < lookup);
            if (!restored) Assert.Equal("? _authorizationService.AuthorizeAsync", authorization.Label);
            else
            {
                Assert.Equal("ControllerBase.get_User", added[0].Label);
                var implementation = Assert.Single(authorization.Children);
                var completion = Assert.Single(implementation.Children, node => node.Label == "if (task.IsCompletedSuccessfully)");
                Assert.Equal("Task.get_IsCompletedSuccessfully", implementation.Children[implementation.Children.ToList().IndexOf(completion) - 1].Label);
                Assert.Equal(["Task<TResult>.get_Result", "AuthorizationResult.get_Succeeded", "Task.FromResult"], completion.Children.Select(node => node.Label));
                Assert.Equal("depth-limit", implementation.Children[^1].Omission!.Reason);
            }
        }
        if (!focused) return;
        var info = Assert.Single(result.Trees, node => node.Label == "AdminController.IndexInfo");
        Consecutive(info, "IndexProfile.get_IndexFullName", "ElasticsearchIndexManager.GetIndexInfoAsync");
        Consecutive(info, "IndexProfile.get_Name", "IndexInfoViewModel.set_IndexDisplayText", "IndexInfoViewModel.set_Id", "IndexInfoViewModel.set_IndexInfo");
        var queryIndex = Assert.Single(result.Trees, node => node.Label == "AdminController.QueryIndex");
        Consecutive(queryIndex, "IndexProfile.get_Id", "AdminQueryViewModel.set_Id");
        Consecutive(queryIndex, "AdminQueryViewModel.set_DecodedQuery", "AdminQueryViewModel.set_Parameters", "AdminController.Query");
        var query = Assert.Single(queryIndex.Children, node => node.Label == "AdminController.Query");
        Assert.Equal('-', Assert.Single(query.Children, node => node.Label == "possible initialization of ElasticsearchPermissions").Mark);
        Assert.Equal(' ', Assert.Single(query.Children, node => node.Label.EndsWith("AuthorizeAsync", StringComparison.Ordinal)).Mark);
        var configuration = Assert.Single(query.Children, node => node.Label == "ElasticsearchConnectionOptions.ConfigurationExists");
        Assert.Equal(["Nullable<T>.get_HasValue", "if (!_isConfigured.HasValue)", "Nullable<T>.get_Value"], configuration.Children.Select(node => node.Label));
        var configured = configuration.Children[1];
        Consecutive(configured, "ElasticsearchConnectionOptions.get_Url", "string.IsNullOrEmpty");
        Assert.Equal(["ElasticsearchConnectionOptions.get_Url", "Uri.TryCreate"], configured.Children[^1].Children.Select(node => node.Label));
        Consecutive(query, "Stopwatch.get_Elapsed", "AdminQueryViewModel.set_Elapsed");
        if (!restored) return;
        var indexInfo = Assert.Single(info.Children, node => node.Label == "ElasticsearchIndexManager.GetIndexInfoAsync");
        Consecutive(indexInfo, "ElasticsearchClient.get_Indices", "_elasticClient.Indices.GetAsync<GetIndexResponse>");
        Consecutive(indexInfo, "GetIndexResponse.get_Indices", "IReadOnlyDictionary<TKey, TValue>.get_Item", "ElasticsearchClient.get_RequestResponseSerializer", "_elasticClient.RequestResponseSerializer.SerializeToString");
        Consecutive(query, "ControllerBase.get_ModelState", "ModelStateDictionary.get_IsValid", "if (!ModelState.IsValid)");
        var results = Assert.Single(Descendants(query.Children), node => node.Label == "if (results != null)");
        Assert.Equal(["ElasticsearchResult.get_TopDocs", "AdminQueryViewModel.set_Documents", "ElasticsearchResult.get_Fields", "AdminQueryViewModel.set_Fields",
            "ElasticsearchResult.get_Count", "AdminQueryViewModel.set_Count"], results.Children.Select(node => node.Label));
    }

    private static void Consecutive(DiffNode parent, params string[] labels)
    {
        var children = parent.Children.Select(node => node.Label).ToList();
        var start = children.IndexOf(labels[0]);
        Assert.True(start >= 0);
        Assert.Equal(labels, children.Skip(start).Take(labels.Length));
    }

    private static IEnumerable<DiffNode> Descendants(IEnumerable<DiffNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Descendants(node.Children)));
}
