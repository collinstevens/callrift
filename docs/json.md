# JSON output v1

Use `--format json` with `diff`, `tree`, or `reach`. The [schema](../schemas/output-v1.schema.json) defines the public envelope. Version 1 allows new optional fields; incompatible changes require a new schema version.

`trees` holds diff/tree results; `paths` holds reach results as root-to-target trees. IDs follow deterministic preorder traversal. Text-only context elision, repeated-subtree suppression, and sibling merging do not alter JSON. Depth and external/test selection still apply.

Each diff node carries separate `before` and `after` evidence. A missing side is null. `symbolId` is the full identity, including parameter types and generic method arity; `label` is for display. `origin` distinguishes source, metadata, structural branch nodes, and unknown binding. In source mode, the `source::` identity prefix names the compilation scope; use `origin` to distinguish package/BCL symbols from repository declarations.

`definition` identifies the declaration when available. `callSites` identifies the invocation or condition. Paths are repository-relative with forward slashes. Positions are one-based and end positions are exclusive. Implicit constructors and unresolved calls may have no declaration location. Diagnostics from both revisions are deduplicated by their full values, including spans.

`dispatch: possible` lists candidate implementation IDs. It does not certify the runtime container chooses a particular implementation. Callback relationships are explicit, but callbacks are not guaranteed to execute. Cycles have an omission with both a canonical symbol ID and the ancestor's traversal ID. A depth omission sets `truncated`; reach also sets it if more paths exist than `--max-paths` permits. A truncated empty path set is not proof of unreachability.

Source-only coverage is always `partial`: it uses one compilation, BCL references, fixed language parsing, and synthetic SDK usings. Inspect limitations and diagnostics before relying on an empty diff. `--strict` returns exit 2 for partial coverage or truncation, including source-only runs without binding diagnostics. Normal analysis returns 0; `diff --exit-code` returns 1 when a change is present.

Revision identities contain resolved commit IDs. Index and working-tree identities contain deterministic SHA-256 digests of ordered analysis inputs. A merge-base comparison records the requested range and the resolved base commit. Digests describe captured input, not a guarantee that the working tree remained unchanged after capture.
