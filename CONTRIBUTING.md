# Contributing to callrift

Install [mise](https://mise.jdx.dev/), then run:

```sh
mise install
mise run hooks:install
mise run build
mise run test
mise run check
```

hk enforces EditorConfig and .NET formatting before commits, conventional commit messages, and build/scenario checks before pushes. Fix formatting with `mise run format`. Project tools are pinned in `mise.toml`; `global.json` selects the same .NET SDK.

The implementation targets .NET 11 with SDK `11.0.100-rc.1.26425.128`. mise also installs SDK 10.0.303 for pinned historical projects used by real-world cases. Existing net10.0 workspace fixtures exercise analysis of older target frameworks from the .NET 11 tool.

The repository enables mise's [tool-path precedence](https://mise.jdx.dev/configuration/settings.html#activate_aggressive). Child processes, including benchmark workers, use the managed SDK even when a system installation is earlier in the inherited PATH.

Scenario tests create temporary Git repositories with before/after commits. These throwaway fixture commits are unsigned locally and in CI. Do not add signing requirements or signing-key setup for them. GitHub Actions initializes only a test identity and line-ending settings through `.github/scripts/Initialize-TestGit.ps1`. Project repository commits remain signed.

Run real-history snapshots with `mise run cases`. They clone the manifest's repositories without checkout into the user cache; set `CALLRIFT_CASES_CACHE` to override its location. Do not vendor upstream source. Review each text/Markdown and JSON snapshot against the scenario description or `git show` for its pinned commit before promoting a received file. Record real-world case review reasoning under `real-world-cases/reviews/`. Never bulk-accept unread snapshots.

Run `mise run benchmark -- --filter '*FloorBenchmarks*' --job short` for the initial floor suite. Performance claims require comparable before/after results and allocation measurements. Keep timing data outside snapshots.

Run `mise run workspaces` for restored project and generator checks, `mise run pack` for installation checks, and `mise run sweep` for a crash sweep over recent upstream history. MSBuild checks restore packages and execute project targets and generators. The published repository runs CI on Ubuntu, Windows, and macOS.

All contributions require an approved contributor agreement before merge. The agreement and legal copyright holder are not finalized yet. Outside contributions must wait for that agreement and a required CLA Assistant check. The agreement must explicitly address copyright assignment if the project retains the proposed single-holder policy; a general contribution license alone does not do that.

Use conventional commits and sign commits with your configured signing key. Do not add source comments. Snapshot changes belong with the behavior change and its review rationale.
