# Expanded real-world cases investigation

These are inspected candidates, not accepted snapshots or completed real-world cases. Clones were created with `--filter=blob:none --no-checkout` under the external user-local `Callrift/real-world-cases` cache. No upstream source was copied into this checkout.

License files were read from these immutable revisions on September 24, 2026. Each accepted historical pair still needs its own license evidence.

| Repository | Inspected revision | License | File | Git blob |
|---|---|---|---|---|
| https://github.com/autofac/Autofac | ae9e1e1129b9c22e7ab111381308dcb02f80a8d2 | MIT | LICENSE | e89fb634a4319b9488446ec6fb04e58cdbb1f2ad |
| https://github.com/App-vNext/Polly | 9a81fdc7c1a89d6c45bba2fb7b062b8eeae39eee | BSD-3-Clause | LICENSE | 620c5f1faddf428efafc8049e3e3aee5dbaadcea |
| https://github.com/ThreeMammals/Ocelot | fcebf2b96c1811500037e010ee3b09e25ac39b19 | MIT | LICENSE.md | 7a0788f9c32c526f1de1cf2e8ba0ae5a260c6c62 |
| https://github.com/OrchardCMS/OrchardCore | 4910d1722d33f1b06d9ddb1cf8f6de2aa871dc8a | BSD-3-Clause | LICENSE | 183936c000fa2cc5969ba724afc1bdc5bfae5080 |
| https://github.com/dotnet/aspnetcore | a58827bd1ee664f61214baf006370254a5209954 | MIT | LICENSE.txt | 984713a49622a96da110443c15477613bc12656b |

Autofac's inspected global.json permits .NET 10 feature-band roll-forward from 10.0.100. Polly pins 10.0.401; OrchardCore requests at least 10.0.401; ASP.NET Core pins an 11.0 release candidate with Arcade SDKs. Compatible historical revisions and mise-provisioned SDKs are required before counting restored runs. Ocelot's inspected global.json sets only the test runner; project target frameworks still need inspection.

## Autofac held pipeline

Before: `dc2252d59922c8b62f8305edabce96fe6211f786`. After: `ae9e1e1129b9c22e7ab111381308dcb02f80a8d2`. Both LICENSE paths resolve to the MIT blob recorded above. Upstream change: [Autofac #1504](https://github.com/autofac/Autofac/pull/1504).

Inspected the complete source diff and `DefaultRegisteredServicesTracker.cs` around `HoldOrApplyForAdditionalService`, `HoldForAdditionalService`, and the following initialization method. Expected effect: remove the `_ephemeralServiceInfo is null` branch and its `IComponentRegistration.BuildResolvePipeline` call under `additionalInfo.IsSourceQueued(source)`. Preserve `DeferSourceImplementation` in that outer branch and preserve `BeginServiceInfoInitialization` under `!additionalInfo.IsInitializing`. The caller retains its monitor guard and try/finally. Runtime event ordering is the upstream motivation; callrift must not claim to prove that ordering through event dispatch.

Focused selector: `DefaultRegisteredServicesTracker.HoldForAdditionalService`. Source-mode text shows the expected guarded removal and preserves the outer initialization/defer calls. Automatic JSON was generated but has not been reviewed. The restored attempt with SDK 10.0.303 failed during workspace loading with a package-pruning message for System.Diagnostics.DiagnosticSource. A mise installation of SDK 10.0.401 downloaded successfully but could not replace the running .NET host; retry after active .NET processes finish. No fallback or snapshot acceptance occurred.

The initial partial-clone read fetched missing blobs individually. That run was stopped, and the two pinned trees' blobs were fetched in one batch without a checkout. The subsequent source command completed. This is cache preparation evidence, not a benchmark or speedup claim.
