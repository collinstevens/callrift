# Closed generic implementation constraints

A directly bound `IHandler<int>` call previously included `Constrained<T>.Run` even when its declaration required `where T : class`. Changing only that impossible implementation incorrectly marked the caller affected. Tree and reach output also included the impossible target. The same problem affected value, unmanaged, constructor, base-type, and interface constraints.

The graph now carries generic parameter constraints and type traits. Dispatch checks the type bindings established by invariant contract and receiver matching. Base and interface constraints follow substituted inheritance, including boxing conversions. Unmanaged constraints inspect substituted instance fields of generic structs. Constructor constraints account for accessibility, abstract classes, inherited required members, and `SetsRequiredMembers`. Ref-like arguments require the corresponding allowance.

Existing public record constructors remain available. The rendered JSON schema is unchanged. The MSBuild worker transports the additional graph metadata with the same package version as its parent.

The regression fixtures define compatible and incompatible types independently, then exercise the real CLI's diff, tree, and reach commands in source and restored modes. They require zero diagnostics, verify the affected root and possible dispatch targets, and check reachability to a changed sink. Thirty-four type examples cover nullable values, arrays, enums, generic fields, inherited constraints, required members, ref-like types, and framework metadata types. Four additional cases change only the constraint and verify caller dispatch changes in both directions and modes.

Before the fix, selected incompatible controls and all four constraint-only edits fail. Compatible controls pass. The isolated prototype passed 59 existing compatibility checks and all 22 workspace checks. The integrated build passed all 72 new cases in 11m4s. Package smoke and formatting passed. All 79 corpus checks passed unchanged in 34m31s. Full scenario push-hook and actual cross-platform CI results remain pending.

This change covers closed invariant bindings. Open parameter relationships, inference through variance, and preservation of generic caller context remain separate audit work. Unknown type information stays conservative. The [generic caller substitutions issue](../issues/generic-context.md) remains open. No performance improvement is claimed.
