# JSON output v1

Use `--format json` with `diff`, `tree`, or `reach`. The [schema](../schemas/output-v1.schema.json) defines the public envelope. Version 1 allows new optional fields; incompatible changes require a new schema version.

`trees` holds diff/tree results; `paths` holds reach results as root-to-target trees. IDs follow deterministic preorder traversal. Text-only context elision, repeated-subtree suppression, and sibling merging do not alter JSON. Depth and external/test selection still apply.

Each diff node carries separate `before` and `after` evidence. A missing side is null. `symbolId` is the canonical identity; ordinary method identities include parameter types and generic method arity. `label` is for display and need not be unique. `origin` distinguishes source, metadata, structural branch nodes, and unknown binding. In source mode, the `source::` identity prefix names the compilation scope; use `origin` to distinguish package/BCL symbols from repository declarations.

MSBuild mode uses `project:<path>@<framework>::` for included projects and `metadata:<assembly>::` for external assemblies. Generated syntax is source in this schema; its normalized path identifies its generated location. MSBuild coverage includes possible virtual dispatch to compatible source implementations. It remains partial: receiver values and enclosing type guards are not propagated, and accessor, operator, event, and framework-convention execution is not followed. The obsolete `virtual-nonabstract-dispatch` limitation is no longer emitted; the schema remains version 1.

Members of file-local types, including nested types, add `/file:<logical-path>` to their scope. Compiler-selected interceptors in file-local types instead use `<scope>::<interceptor>:<digest>`. The digest identifies the intercepted source calls rather than a generated private type name. Consumers should treat symbol IDs as opaque strings. These identity corrections do not change the version 1 schema.

`definition` identifies the declaration when available. `callSites` identifies the invocation or condition. Paths are repository-relative with forward slashes. Positions are one-based and end positions are exclusive. Implicit constructors and unresolved calls may have no declaration location. Diagnostics from both revisions are deduplicated by their full values, including spans.

`dispatch: possible` lists candidate implementation IDs. It does not certify the runtime container chooses a particular implementation. Callback relationships are explicit, but callbacks are not guaranteed to execute. Cycles have an omission with both a canonical symbol ID and the ancestor's traversal ID. A depth omission sets `truncated`; reach also sets it if more paths exist than `--max-paths` permits. A truncated empty path set is not proof of unreachability.

Returned or assigned callback bodies appear beneath a structural node with `relation: callback`. Its child calls describe potential execution after delegate creation. A returned method group's receiver is evaluated before that node. This representation does not track delegate values through later assignments or prove that any consumer invokes them.

Source-only coverage is always `partial`: it uses one compilation, BCL references, fixed language parsing, and synthetic SDK usings. Inspect limitations and diagnostics before relying on an empty diff. `--strict` returns exit 2 for partial coverage or truncation, including source-only runs without binding diagnostics. Normal analysis returns 0; `diff --exit-code` returns 1 when a change is present.

Revision identities contain resolved commit IDs. Index and working-tree identities contain deterministic SHA-256 digests of ordered analysis inputs. A merge-base comparison records the requested range and the resolved base commit. Digests describe captured input, not a guarantee that the working tree remained unchanged after capture.
