# Git workflow

- Work and commit directly on `master`.
- Do not create other branches or pull requests.
- Push signed checkpoints to `origin/master` regularly.

# Validation

- Gate commits with EditorConfig and formatting checks; keep conventional commit-message validation. Formatting hooks must not restore; prepare assets explicitly when dependencies change.
- Do not gate pushes on builds or tests.
- Run focused tests, builds, and benchmarks locally when relevant to the changed area.
- Use CI for delayed feedback from the full end-to-end suite. Do not require a full local suite before committing or pushing.
- Use `mise run test:fast` (also `mise run test`) for routine semantic feedback. Migration is ongoing, so also run affected non-fast coverage.
- Use `mise run test:integration`, `test:focused`, `workspaces:focused`, or `cases:focused` with a specific filter; select benchmark filters for the affected area.
- Use `mise run test:e2e` only for an explicit broad run. Empty or invalid focused selections must fail.
- Push after appropriate focused validation and continue independent work while CI runs. Do not stall a checkpoint waiting for the full suite.
- Do not repeat passing checks unless a relevant change or unresolved failure justifies another run. Report pending or failed CI accurately.
