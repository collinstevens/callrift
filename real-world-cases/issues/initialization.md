# Initialization audit limits

The constructor correction indexes default, primary, and record-copy constructors and binds implicit base calls. It preserves source locations, instance initializer order, constructor chaining, duplicate-body omissions, and unresolved-base diagnostics. These changes do not complete the initialization audit.

At OrchardCore workflow pin `ee892e7e9fb30305110504bf553dcf4265e6c02a`, `src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Entities/Entity.cs` initializes `Properties` with an empty collection expression. `ActivityRecord` inherits `Entity`; the reviewed tree now exposes that source base call and its object base. The call collector does not model the collection expression's lowered construction. `Role.RoleClaims` and `AdminQueryViewModel.Documents`/`Fields` provide additional empty-collection examples. Their initializer syntax was inventoried during review; snapshot acceptance does not establish coverage of the lowered calls.

Record-class `with` expressions now follow the compiler-selected clone and copy constructor. The [record-copy review](../reviews/record-copy.md) covers receiver constraints, initializer ordering, declaration validation, and the corpus diagnostics. Record structs and anonymous objects do not acquire a record-class copy call.

Static field, property, and event initializer expressions and explicit static constructors now have source call paths. The [static-initialization review](../reviews/static-initialization.md) describes trigger binding, partial declaration order, closed generic state, and conditional paths. Initialization remains possible execution rather than a runtime schedule.

Property/indexer and event accessor execution, user-defined operators and conversions, collection-expression lowering, and broader compiler-error reporting still require implementation or precise reviewed coverage reporting. Partial coverage remains explicit in both analysis modes. These are open engineering requirements, not exclusions from the implementation goal.
