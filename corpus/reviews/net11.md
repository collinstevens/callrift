# .NET 11 snapshot review

Reviewed the five pinned CleanArchitecture source diffs in `corpus/manifest.json` against SDK 11.0.100-rc.1.26425.128. Source analysis references the running framework assemblies. The .NET 11 runtime includes Microsoft.Extensions logging, dependency injection, configuration, and hosting abstractions that were missing from the .NET 10 reference set.

| Pair | Diagnostics before / after migration | Reviewed output change |
|---|---|---|
| endpoint-groups | 400 / 380 | Authorization movement stays unchanged; framework binding removes diagnostics elsewhere in the repository. |
| logging-di | 327 / 304 | Registration changes stay unchanged; the root parameter now resolves to Microsoft.Extensions.Hosting.IHostApplicationBuilder. |
| validation-lambda | 325 / 305 | Constructor movement into the callback and guard changes stay unchanged. |
| handler-rename | 319 / 297 | Eight removed/added roots remain. Constructor ILogger types become fully qualified. Four logging calls resolve to metadata and follow the default external-call filter. |
| guard-library | 851 / 832 | Source exception replacement with the unresolved Ardalis guard stays unchanged. Improved binding changes the diagnostic summary. |

Compared the text, Markdown, and JSON snapshots against the actual source changes. The JSON envelope, source locations, and unaffected trees remain intact. Calls involving missing Entity Framework types still report unresolved binding, with AddScoped candidates now available. Missing MediatR, FluentValidation, and Ardalis dependencies remain visible as limitations.

Ran the handler rename with `--externals` and confirmed all four LogInformation calls retain their source spans and report resolved metadata binding to Microsoft.Extensions.Logging.LoggerExtensions. Validated each updated JSON snapshot against `schemas/output-v1.schema.json`. Accepted only these five named text/Markdown snapshots and their five JSON counterparts.
