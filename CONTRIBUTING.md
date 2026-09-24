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

The implementation targets .NET 11 with SDK `11.0.100-rc.1.26425.128`. mise also installs SDK 10.0.303 for pinned historical corpus projects. Existing net10.0 workspace fixtures exercise analysis of older target frameworks from the .NET 11 tool.

The repository enables mise's [tool-path precedence](https://mise.jdx.dev/configuration/settings.html#activate_aggressive). Child processes, including benchmark workers, use the managed SDK even when a system installation is earlier in the inherited PATH.

Scenario tests create temporary Git repositories with before/after commits. Local runs inspect your Git identity and signing configuration and sign fixture commits with your configured key. GitHub Actions uses unsigned temporary fixture commits and initializes only a test identity through `.github/scripts/Initialize-TestGit.ps1`.

Run real-history snapshots with `mise run corpus`. They clone the manifest's repositories without checkout into the user cache; set `CALLRIFT_CORPUS_CACHE` to override its location. Do not vendor corpus source. Review each text/Markdown and JSON snapshot against the scenario description or `git show` for its pinned commit before promoting a received file. Record corpus review reasoning under `corpus/reviews/`. Never bulk-accept unread snapshots.

Run `mise run benchmark -- --filter '*FloorBenchmarks*' --job short` for the initial floor suite. Performance claims require comparable before/after results and allocation measurements. Keep timing data outside snapshots.

Run `mise run workspaces` for restored project and generator checks, `mise run pack` for installation checks, and `mise run sweep` for a crash sweep over recent corpus history. MSBuild checks restore packages and execute project targets and generators. The checked-in CI matrix runs on Ubuntu, Windows, and macOS; remote runs begin after the repository is published.

All contributions require an approved contributor agreement before merge. The agreement and legal copyright holder are not finalized yet. Outside contributions must wait for that agreement and a required CLA Assistant check. The agreement must explicitly address copyright assignment if the project retains the proposed single-holder policy; a general contribution license alone does not do that.

Use conventional commits and sign commits with your configured signing key. Do not add source comments. Snapshot changes belong with the behavior change and its review rationale.
