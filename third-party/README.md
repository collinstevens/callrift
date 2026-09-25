# Dependency notices

These notices apply to dependencies distributed with the tool. They do not replace the repository's LICENSE.

- Microsoft.CodeAnalysis packages and .NET libraries: `DotNet.txt` and package-specific ThirdPartyNotices files.
- System.CommandLine: `CommandLine.txt`.
- Microsoft.Build.Locator: `MSBuildLocator.txt`.
- Humanizer.Core: `Humanizer.txt`.
- Microsoft.VisualStudio.SolutionPersistence and Microsoft-authored supporting libraries use MIT terms; the Microsoft copyright notice is retained in `MSBuildLocator.txt`.

Exact package versions are pinned in [Directory.Packages.props](../Directory.Packages.props) and the project lockfiles. License texts were retrieved from their upstream repositories; package-specific notices come from the pinned NuGet archives. The tool uses the installed SDK's MSBuild implementation through Locator.
