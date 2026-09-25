# ASP.NET Core source coverage

Source-only mode combines C# files into one compilation with BCL references. It does not evaluate ASP.NET Core's project references, shared-source includes, framework defines, source generators, Razor compilation, or runtime framework conventions. Package and template inputs produce unresolved calls and parse diagnostics. Application fixtures remain included; assertion test projects are excluded using project evidence.

The accepted JavaScript task-completion pair preserves these diagnostics and explicit depth omissions. Reflection is represented by the visible calls to reflection APIs; inferred runtime reflection targets and Native AOT execution are not claimed. Property bodies and generated serializer implementations are not followed.

A separate, unaccepted authentication-scheme candidate demonstrates cross-project type shadowing. At `26e9547de9301d6da2aa0f3a1a52eca6350060c5`, `Utf8HashLookup.GetCaseSensitiveHashCode` constructs an unqualified `HashCode`, invokes `AddBytes`, and invokes `ToHashCode`. The single source compilation binds the constructor and `ToHashCode` to the global shim in `src/Shared/HashCode.cs`, even though the SignalR Core project does not include that shim. `AddBytes` becomes unresolved. This candidate remains outside the accepted manifest; reducing depth would not establish correct binding.

Other unaccepted ASP.NET Core candidates expose the [generic caller substitution issue](generic-context.md). In the CTS-disposal pair, a dictionary keyed by `ModelMetadata` incorrectly reaches a comparer for `TagHelperAttribute` after expansion loses the generic caller's binding. Large automatic root sets are not accepted merely because focused selectors produce smaller output.

Historical MSBuild materialization currently rejects symlinks and gitlinks. The JavaScript task pair's explicit restored trial fails on `src/submodules/MessagePack-CSharp`. Its source result is not a fallback presented as restored analysis. Supported project materialization and actual cross-platform repeats remain separate requirements.
