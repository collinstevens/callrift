# Test-framework assembly reference classification

Status: implemented and locally validated; existing snapshots are unchanged.

ASP.NET Core's NativeAotTestApp.E2E.Tests project at `5f5f31f9a84de3461271a56f726e9b532810b071` declares OutputType Exe and a direct Reference to MSTest.TestFramework. Its test infrastructure supplies the framework through imported build targets. Source classification previously recognized only PackageReference items, so it treated the executable as an application and exposed NativeAotFeatureTests.JsInterop_AsyncReturnTypePromisesResolve in default affected-root output.

The classifier now recognizes direct references to MSTest.TestFramework, Microsoft.VisualStudio.TestPlatform.TestFramework, nunit.framework, xunit.core, and xunit.v3.core. The latter four assembly names were checked against locally installed framework packages. Assembly-qualified Include values retain the assembly-name match. Package-reference handling is unchanged.

An explicit IsTestProject=false still wins. Conditional references remain uncertain and produce the existing inference diagnostic. Microsoft.AspNetCore.TestHost alone does not identify a test project, and application executables beneath test directories remain included. The --tests option restores excluded test callers.

Eight independently specified baseline cases produced six failures and two passes. Five failures exposed incorrectly included test callers; one exposed the missing conditional-metadata diagnostic. All nine final cases and fifteen existing classification scenarios pass. The final suite adds an assembly-qualified reference case and checks text, Markdown, and JSON.

The ASP.NET Core reproduction now has five application/library roots instead of six. The test-method root is removed; the original five other root identities remain. The 636 removed diagnostics all belong to NativeAotTestApp.E2E.Tests or TestApp.E2E.Tests. Both project files were inspected and contain direct MSTest.TestFramework references. No new diagnostics appear. Build, formatting, and package smoke checks pass. All fifty-one pinned real-world checks pass without snapshot changes in 14m23s. Actual CI verification remains separate from this local evidence.

This correction does not evaluate arbitrary imported MSBuild targets or classify projects solely by their names. ASP.NET Core trial pairs remain unaccepted and their restored analysis is explicitly unsupported by the current submodule materializer.
