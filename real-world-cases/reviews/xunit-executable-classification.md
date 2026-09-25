# Executable xUnit project classification

Review date: 2026-09-24. This correction adds no accepted revision pairs or snapshots.

OrchardCore's `test/OrchardCore.Tests/OrchardCore.Tests.csproj` declares `OutputType` as `Exe` and references `xunit.v3.mtp-v2`. The source classifier recognized `xunit.v3` but missed its MTP-specific packages. It consequently treated the test executable as an application and included its callers and implementations by default.

The inspected breadcrumb pair is `cd7d8430905d2f2ab3ba3aef49577d73647ccfbf` to `4910d1722d33f1b06d9ddb1cf8f6de2aa871dc8a` in [OrchardCore](https://github.com/OrchardCMS/OrchardCore). Both revisions have the inspected BSD-3-Clause LICENSE blob `183936c000fa2cc5969ba724afc1bdc5bfae5080`. The same executable/package metadata is present at all ten revisions of the five screened candidates. Upstream source and restored assets remain outside this checkout.

The classifier now recognizes the exact top-level MTP and Native AOT framework package names documented by xUnit: [MTP variants](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform) and [AOT variants](https://xunit.net/docs/getting-started/v3/native-aot). An unconditional framework reference identifies a test executable. Explicit `IsTestProject` overrides still take precedence; conditional metadata remains uncertain. Executables without test markers remain application inputs, including applications under test directories. `--tests` restores test inputs.

Six CLI regressions failed before the correction because `Checks.Main` appeared in default output. After the correction, all six pass across text, Markdown, and JSON. They also confirm that `Sample.Main` remains visible and that `--tests` includes both callers. The nine existing classification checks pass alongside them, including source/restored analysis, literal overrides, and conditional metadata (15 checks, 2026-09-24 22:35:21 run).

The corrected breadcrumb source trial excludes 381 former test roots. Its total changes from 685 roots and 30,304 diagnostics to 379 roots and 16,224 diagnostics. Removing test implementations and callers also changes affected production roots; the result is not a simple removal of test-root rows. These exploratory OrchardCore graphs remain unaccepted and require a complete semantic review. Separate restored trials exposed random generated interceptor identities that also need correction before acceptance.

All 30 accepted source-mode views and two restored Polly telemetry views passed unchanged (32 checks, 2026-09-24 22:36:12 run, 3 minutes 33 seconds). The restored telemetry views were also selected because the case ID contains source. Formatting passes. No performance claim is made from the exploratory runs.
