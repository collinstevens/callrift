# Release procedure

Local packages are built and exercised with `mise exec -- pwsh -File scripts/Package-Smoke.ps1`. The script packs `callrift` and `callrift.core`, installs the tool into a disposable artifacts directory, analyzes this repository in both modes, and runs `dnx` from the local feed. It does not publish or modify the global tool installation.

The release workflow defaults to artifact creation. Publishing requires an explicit workflow-dispatch selection and a `nuget` environment. Before enabling publication, configure required reviewers for that environment and add its scoped `NUGET_API_KEY` secret. Review the exact version, package contents, dependency notices, test results, and benchmark evidence before approving the publish job. Do not reuse a published package version.

The repository is published at `collinstevens/callrift`. Signed checkpoints on `origin/master` are authorized. NuGet publication and unrelated account changes are not authorized. The README's public-feed installation commands become usable after a release exists.

Before accepting the first outside contribution, finalize the legal copyright holder and contributor assignment agreement, configure CLA Assistant, and make its check required by branch protection. The local contribution policy already blocks outside merges until those steps are complete. Configure conventional PR titles for squash merges and require the three-platform CI checks.

Temporary fixture commits are unsigned locally and in CI. The CI setup configures a test identity and line-ending settings; it requires no signing key. Project repository commits remain signed with the maintainer's configured key.

The owner must supply the legal holder's full name (or entity name and jurisdiction), the intended rights policy (copyright assignment or a contribution license permitting commercial relicensing), the approved agreement text and version, and the contact responsible for agreement administration. The owner must also approve the CLA Assistant service/account connection and the required-check configuration. These legal and account decisions remain external release blockers. The repository currently has a contribution policy, not automated CLA enforcement.

The scheduled crash sweep records failures and timings without creating snapshots. The benchmark workflow uploads full BenchmarkDotNet exports and flags generous time/allocation thresholds as warnings. Hosted runners differ from the recorded development machine; investigate a warning with same-machine measurements before calling it a regression.
