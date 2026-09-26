# Git workflow

- Work and commit directly on `master`.
- Do not create other branches or pull requests.
- Push signed checkpoints to `origin/master` regularly.

# Validation

- Gate commits with EditorConfig and formatting checks; keep conventional commit-message validation.
- Do not gate pushes on builds or tests.
- Run focused tests, builds, and benchmarks locally when relevant to the changed area.
- Use CI for delayed feedback from the full end-to-end suite. Do not require a full local suite before committing or pushing.
- Use `mise run test:focused`, `workspaces:focused`, or `cases:focused` with a specific filter; select benchmark filters for the affected area.
- Push after appropriate focused validation and continue independent work while CI runs. Do not stall a checkpoint waiting for the full suite.
- Do not repeat passing checks unless a relevant change or unresolved failure justifies another run. Report pending or failed CI accurately.
