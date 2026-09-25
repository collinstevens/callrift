# OrchardCore Elasticsearch authorization review

Status: reviewed and accepted. All four source/restored automatic/focused views passed an independent replay in 8m23s.

The pair compares `ca940915513382013d941ddeb86267cfcfe4df75` with `008eb1e7da318fb28a80b40083e01ef57480bccb` in `OrchardCMS/OrchardCore`. Both revisions contain the BSD-3-Clause license blob `183936c000fa2cc5969ba724afc1bdc5bfae5080`. The production diff changes only the Elasticsearch admin controller. Namespace-import ordering changes have no call-flow effect.

`IndexInfo(string)` and `QueryIndex(string, string, string)` gain `AuthorizeAsync(User, ManageElasticIndexes)` before index lookup. An unsuccessful authorization returns `Forbid()`. Index lookup, the null-index return, JSON formatting, view construction, query decoding, and delegation to the existing `Query(AdminQueryViewModel)` method remain in place. The query helper already performs its own authorization. Its unchanged call remains visible in the deeper JSON view.

Both automatic views independently discover exactly these two endpoints. The source view retains unresolved authorization and MVC calls with explicit diagnostics. Restored analysis binds OrchardCore's two authorization-extension overloads, including the null-user path, `PermissionRequirement`, framework authorization call, completed-task path, and asynchronous local helper. The restored automatic view excludes external calls by default, so its guard containing only `Forbid()` is absent. The focused restored view enables externals and shows that guard. Focused selectors do not replace automatic discovery.

| View | Roots | Nodes | Added nodes | Diagnostics |
|---|---:|---:|---:|---:|
| Source automatic | 2 | 22 | 6 | 16,195 |
| Source focused | 2 | 120 | 6 | 16,195 |
| Restored automatic | 2 | 13 | 2 | 0 |
| Restored focused | 2 | 138 | 22 | 0 |

Every remaining node is unchanged. All four views report depth truncation and contain no cycle omission. Source diagnostics include 16,174 unresolved calls, nineteen inferred test-project classifications, and two duplicate-member reports from the repository-wide single compilation. Restored views select `src/OrchardCore.Modules/OrchardCore.Elasticsearch/OrchardCore.Elasticsearch.csproj` at `net10.0`. The historical SDK request is `10.0.302` with `latestMajor` roll-forward; the recorded restored runs use the mise-managed SDK installation.

The review checked the source diff, surrounding endpoint and authorization-helper bodies, added guards and targets, preserved calls, all change markers, declaration identities, and diagnostics. Text and Markdown trees agree exactly after removing fences. JSON includes unchanged context omitted by the rendered context window. The location audit checks 670 node locations and 32,390 diagnostic locations across these four views against immutable Git blobs. No generated definition occurs in these views. Review hashes are in `artifacts/orchardcore-elasticsearch-authorization-output-review.json`.

The analysis describes possible calls and syntax-level guards. It does not prove deployed authorization configuration or runtime access decisions. Source-only binding, implicit member execution, and framework conventions remain covered by the existing [OrchardCore coverage issue](../issues/orchardcore-coverage.md). Package binding is evidenced by restored runs, not inferred from the source-only output.
