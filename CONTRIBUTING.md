# Contributing to callrift

Install [mise](https://mise.jdx.dev/), then run:

```sh
mise install
mise exec -- dotnet restore callrift.slnx --locked-mode
mise run hooks:install
mise run check
```

hk checks EditorConfig and .NET formatting before commits and validates conventional commit messages. Formatting hooks use `--no-restore`; restore explicitly after dependency changes or when preparing a new checkout. Pushes do not run builds or tests. Rerun `mise run hooks:install` after pulling hook changes to remove stale local registrations. Fix formatting with `mise run format`. Project tools are pinned in `mise.toml`; `global.json` selects the same .NET SDK.

Run `mise run test` (or `mise run test:fast`) for routine semantic feedback. The fast tier covers 350 semantic and boundary cases; its coverage accounting and measurements are listed in [development-performance.md](docs/development-performance.md). Also select affected integration coverage when changing Git, CLI, project loading, generators, or process behavior. Run focused tests, builds, and benchmarks locally when they are relevant to the change. For example, validate a dispatch change with `mise run test:focused -- 'FullyQualifiedName~ConstraintDispatchTests'`. Use `mise run build` for changes that need a solution build and select benchmark filters for the affected performance area. Documentation-only changes need formatting checks. Use `mise run check:changed` for unstaged changes; commit hooks check staged files automatically. CI runs fast tests, a bounded integration/packaging selection and formatting in independent jobs, with the remaining end-to-end coverage providing delayed feedback; do not wait for a full local suite before committing or pushing.

The focused test tasks require a filter and build their test project as needed. Empty, invalid or unmatched selections fail. `test:integration` restricts the supplied filter to non-fast scenario rows; `test:focused` can select either layer. `test:e2e` explicitly runs all non-fast scenarios, workspaces and pinned cases. `test:fast` and `test:e2e` together retain the full suite. No tests have been added to hooks. For additional dotnet test options, invoke `mise exec -c 'dotnet test <project> --filter <filter> ...'` directly. Use the full-suite tasks only when explicitly needed for diagnosis or a requested complete run.

| Changed area | Local command |
|---|---|
| Routine semantic feedback | `mise run test:fast` |
| Scenarios and semantic behavior | `mise run test:focused -- 'FullyQualifiedName~ConstraintDispatchTests'` |
| Workspaces, packages, and generators | `mise run workspaces:focused -- 'FullyQualifiedName~InterceptorTests'` |
| A pinned real-world case | `mise run cases:focused -- 'DisplayName~aspnetcore-stream-cts-disposal'` |
| Compilation or project wiring | `mise run build` |
| A performance area | `mise run benchmark -- --filter '*FloorBenchmarks.ReadBlobs*' --job short` |
| Unstaged formatting | `mise run check:changed` |

Do not repeatedly run the same passing checks without a relevant new change or unresolved failure. Push signed checkpoints after appropriate focused validation and continue useful work while CI runs. Report pending or failed CI accurately; a quick local loop does not establish full-suite success.

The implementation targets .NET 11 with SDK `11.0.100-rc.1.26425.128`. mise also installs SDKs 10.0.303 and 10.0.401 for historical projects used by real-world investigations. Workspace fixtures exercise older target frameworks and mixed-framework project references from the .NET 11 tool.

The repository enables mise's [tool-path precedence](https://mise.jdx.dev/configuration/settings.html#activate_aggressive). Child processes, including benchmark workers, use the managed SDK even when a system installation is earlier in the inherited PATH.

CLI scenario tests create temporary Git repositories with before/after commits. Direct semantic tests analyze in-memory sources, while component fixtures restore isolated project snapshots and reuse their analyzed graphs. These throwaway fixture commits are unsigned locally and in CI. Do not add signing requirements or signing-key setup for them. GitHub Actions initializes only a test identity and line-ending settings through `.github/scripts/Initialize-TestGit.ps1`. Project repository commits remain signed.

Run real-history snapshots with `mise run cases`. They clone the manifest's repositories without checkout into the user cache; set `CALLRIFT_CASES_CACHE` to override its location. Do not vendor upstream source. Review each text/Markdown and JSON snapshot against the scenario description or `git show` for its pinned commit before promoting a received file. Record real-world case review reasoning under `real-world-cases/reviews/`. Never bulk-accept unread snapshots.

Run `mise run benchmark -- --filter '*FloorBenchmarks*' --job short` for the initial floor suite. Performance claims require comparable before/after results and allocation measurements. Keep timing data outside snapshots.

Run `mise run workspaces` for restored project and generator checks, `mise run pack` for installation checks, and `mise run sweep` for a crash sweep over recent upstream history. MSBuild checks restore packages and execute project targets and generators. The published repository runs CI on Ubuntu, Windows, and macOS. CI runs fast tests independently on all three operating systems. Slow CI runs the remaining scenario, workspace, and real-world suites sequentially within each OS job so their compilations and restores do not compete across suites. Superseded runs on the same branch are cancelled and remain reported as cancelled. Test summaries include the revision, failing cases, reproduction command and complete dotnet invocation time; the wrapper adds PowerShell/mise startup time to the local command. Within the scenario suite, up to four test classes run concurrently. Partial-clone tests mutate process-wide Git environment variables and run in an exclusive collection. Put any future process-wide environment mutations in that collection or eliminate the shared state.

`mise run cases` runs every pinned case and view locally. Set `CALLRIFT_CASE_SET=routine` to use the bounded CI selection. The manifest can exclude a whole case or an individual view from that selection with `routine: false`. Scheduled and manual reviewed-case workflows run the complete set on all three operating systems. New multi-view cases require a review document and immutable license blob identities for both revisions. Each view has a ten-minute analysis timeout.

All contributions require an approved contributor agreement before merge. The agreement and legal copyright holder are not finalized yet. Outside contributions must wait for that agreement and a required CLA Assistant check. The agreement must explicitly address copyright assignment if the project retains the proposed single-holder policy; a general contribution license alone does not do that.

Use conventional commits and sign commits with your configured signing key. Do not add source comments. Snapshot changes belong with the behavior change and its review rationale.
