# Variant dispatch compatibility review

Status: implemented and locally validated; existing snapshots are unchanged.

The original matcher recorded only whether a parameter was variant, then skipped argument matching for that parameter. An `IProducer<string>` caller could consequently include an `IProducer<object>` implementation. Reversed contravariance and boxing conversions were also admitted. Six independently specified source/restored CLI checks failed on the baseline; the two initial valid-inheritance checks passed.

The matcher now retains `in`/`out` direction and checks identity or implicit reference conversions in the required direction. A portable type-definition catalog carries compiler-reported class/interface inheritance, generic base arguments, reference/value classification, and special types across the MSBuild worker. The checks cover nested variance, arrays, and array interfaces. Boxing and user-defined conversions do not establish variant compatibility. Object/dynamic identity is preserved.

The independent expectations follow the C# specification's [variance conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/interfaces#19233-variance-conversion) and [implicit reference conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/conversions#1028-implicit-reference-conversions). CLI checks assert affected roots, displayed targets, and absent reach paths, with valid targets required to remain visible. All twenty-three final cases pass, including object/dynamic and nint/IntPtr identity, incompatible array ranks/elements, generic base substitution, user-defined conversions, and an assembly named `array`. The expanded intermediate suite also passed thirty-two existing generic/receiver/interface/virtual checks.

The final implementation passes all fifty-one pinned real-world checks without snapshot changes, all twenty-two workspace checks, and the package smoke checks. The solution builds without warnings or errors. These results establish local compatibility; actual CI verification remains separate from this review.

Symbol identities and user JSON fields are unchanged. The existing two-argument `DispatchContract.CanMatch` API remains available. The hierarchy data is part of the analysis graph and worker transport. Unknown and unbound types remain conservative. This correction does not resolve [generic caller substitutions](../issues/generic-context.md), generic-parameter constraint inference, or enclosing-guard narrowing.
