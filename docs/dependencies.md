# Dependency audit

The M1 dependency closure was inspected before adding packages. Versions below come from the restored NuGet assets and package metadata. The selected dependencies use MIT or Apache-2.0 terms, compatible with the project's AGPL-3.0-or-later licensing. Keep required dependency attribution in distributed artifacts when packaging is added in M4.

For each expression-based package, the table records its nuspec license expression. Two packages embed MIT license files: CommandLineParser 2.9.1 (`License.md`) and Microsoft.DotNet.PlatformAbstractions 3.1.6 (`LICENSE.TXT`). The older xunit.abstractions package has no expression; its [upstream license](https://github.com/xunit/abstractions.xunit/blob/main/license.txt) identifies Apache-2.0.

Package locks preserve the inspected versions. Recheck the complete closure when changing them, including build, test, and benchmark dependencies. The audit covers NuGet code dependencies; it does not imply that source being analyzed becomes part of Callrift.

| Package/version | Declared license |
|---|---|
| Argon/0.33.5 | MIT |
| BenchmarkDotNet/0.15.8 | MIT |
| BenchmarkDotNet.Annotations/0.15.8 | MIT |
| CommandLineParser/2.9.1 | MIT |
| DiffEngine/18.4.1 | MIT |
| EmptyFiles/8.17.2 | MIT |
| Gee.External.Capstone/2.3.0 | MIT |
| Iced/1.21.0 | MIT |
| Microsoft.CodeAnalysis.Analyzers/5.9.0-1.26328.17 | MIT |
| Microsoft.CodeAnalysis.Common/5.9.0 | MIT |
| Microsoft.CodeAnalysis.CSharp/5.9.0 | MIT |
| Microsoft.CodeCoverage/18.10.1 | MIT |
| Microsoft.Diagnostics.NETCore.Client/0.2.510501 | MIT |
| Microsoft.Diagnostics.Runtime/3.1.512801 | MIT |
| Microsoft.Diagnostics.Tracing.TraceEvent/3.1.21 | MIT |
| Microsoft.DotNet.PlatformAbstractions/3.1.6 | MIT |
| Microsoft.Extensions.DependencyInjection/6.0.0 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions/6.0.0 | MIT |
| Microsoft.Extensions.Logging/6.0.0 | MIT |
| Microsoft.Extensions.Logging.Abstractions/6.0.0 | MIT |
| Microsoft.Extensions.Options/6.0.0 | MIT |
| Microsoft.Extensions.Primitives/6.0.0 | MIT |
| Microsoft.NET.Test.Sdk/18.10.1 | MIT |
| Microsoft.TestPlatform.ObjectModel/18.10.1 | MIT |
| Microsoft.TestPlatform.TestHost/18.10.1 | MIT |
| Perfolizer/0.6.1 | MIT |
| Pragmastat/3.2.4 | MIT |
| SimpleInfoName/3.2.0 | MIT |
| System.CodeDom/9.0.5 | MIT |
| System.CommandLine/2.0.12 | MIT |
| System.Management/9.0.5 | MIT |
| System.Reflection.TypeExtensions/4.7.0 | MIT |
| Verify/31.12.5 | MIT |
| Verify.Xunit/31.12.5 | MIT |
| xunit/2.9.3 | Apache-2.0 |
| xunit.abstractions/2.0.3 | Apache-2.0 |
| xunit.analyzers/1.18.0 | Apache-2.0 |
| xunit.assert/2.9.3 | Apache-2.0 |
| xunit.core/2.9.3 | Apache-2.0 |
| xunit.extensibility.core/2.9.3 | Apache-2.0 |
| xunit.extensibility.execution/2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio/3.1.5 | Apache-2.0 |
