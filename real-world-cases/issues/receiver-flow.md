# Receiver flow and infeasible dispatch candidates

Status: open. Applies to both source and MSBuild modes.

Virtual dispatch lists source implementations compatible with the bound method. Explicit base calls, sealed static receiver types, and directly constructed receivers use direct expansion. The graph does not propagate receiver values or narrow an object's type using enclosing guards.

The pinned `serilog-null-key` pair exposes a specific incorrect candidate set in unchanged context. At `6c3fbcf636b0671bbd6f5032b61a2254937d8408`, `src/Serilog/Formatting/Json/JsonValueFormatter.cs:269` calls `value.ToString()` inside `if (value is char)`. The static type of `value` is `object`. The JSON snapshot lists `LogEventPropertyValue`, `MessageTemplate`, `PropertyToken`, and `TextToken` overrides as possible targets. Those reference-type implementations cannot execute on that guarded path. The before revision has the same issue at line 258. Text's unchanged-context trimming hides this subtree; the JSON review exposes it.

The changed dictionary-key calls on lines 146 (before) and 154 (after) also bind to `object.ToString()`. Their source override candidates are conservative: no active Serilog conversion policy or runtime key value is inferred. Moving the call under the non-null branch is supported evidence; execution of a particular override is not.

Resolution requires flow-sensitive receiver constraints shared by expansion, affected-root discovery, reach, and JSON targets. Review guard narrowing, assignments that invalidate narrowing, joins, negated guards, and callbacks before closing this issue. Keep coverage partial and do not treat a reported potential path as proof of runtime reachability.
