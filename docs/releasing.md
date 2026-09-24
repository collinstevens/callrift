# Release procedure

Local packages are built and exercised with `mise exec -- pwsh -File scripts/Package-Smoke.ps1`. The script packs `callrift` and `Callrift.Core`, installs the tool into a disposable artifacts directory, analyzes this repository in both modes, and runs `dnx` from the local feed. It does not publish or modify the global tool installation.

The release workflow defaults to artifact creation. Publishing requires an explicit workflow-dispatch selection and a `nuget` environment. Before enabling publication, configure required reviewers for that environment and add its scoped `NUGET_API_KEY` secret. Review the exact version, package contents, dependency notices, test results, and benchmark evidence before approving the publish job. Do not reuse a published package version.

Repository creation, pushes, and the first NuGet publication need the owner's approval. No remote repository, package reservation, or publication was performed during local implementation. The README's public-feed installation commands become usable after a release exists.

Before accepting the first outside contribution, finalize the legal copyright holder and contributor assignment agreement, configure CLA Assistant, and make its check required by branch protection. The local contribution policy already blocks outside merges until those steps are complete. Configure conventional PR titles for squash merges and require the three-platform CI checks.

CI provisions a temporary SSH signing key for synthetic fixture commits. It inspects effective signing settings through the fixture harness and fails on signing errors. It does not configure signature verification or use the maintainer's private key.

The scheduled crash sweep records failures and timings without creating snapshots. The benchmark workflow uploads full BenchmarkDotNet exports and flags generous time/allocation thresholds as warnings. Hosted runners differ from the recorded development machine; investigate a warning with same-machine measurements before calling it a regression.
