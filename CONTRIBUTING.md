# Contributing to Callrift

Install [mise](https://mise.jdx.dev/), then run:

```sh
mise install
mise run hooks:install
mise run build
mise run test
mise run check
```

hk enforces EditorConfig and .NET formatting before commits, conventional commit messages, and build/scenario checks before pushes. Fix formatting with `mise run format`. Project tools are pinned in `mise.toml`; `global.json` selects the same .NET SDK.

The repository enables mise's [tool-path precedence](https://mise.jdx.dev/configuration/settings.html#activate_aggressive). Child processes, including benchmark workers, use the managed SDK even when a system installation is earlier in the inherited PATH.

Scenario tests create temporary Git repositories and signed before/after commits. Configure your Git identity and signing key first. Tests inspect those settings and fail if signing fails. Never disable signing or accept unsigned fixture commits. A CI fixture signing identity must be provisioned before enabling these tests there.

Run real-history snapshots with `mise run corpus`. They clone the manifest's repositories without checkout into the user cache; set `CALLRIFT_CORPUS_CACHE` to override its location. Do not vendor corpus source. Review each text/Markdown and JSON snapshot against the scenario description or `git show` for its pinned commit before promoting a received file. Record corpus review reasoning under `corpus/reviews/`. Never bulk-accept unread snapshots.

Run `mise run benchmark -- --filter '*FloorBenchmarks*' --job short` for the initial floor suite. Performance claims require comparable before/after results and allocation measurements. Keep timing data outside snapshots.

All contributions require an approved contributor agreement before merge. The agreement and legal copyright holder are not finalized yet. Outside contributions must wait for that agreement and a required CLA Assistant check. The agreement must explicitly address copyright assignment if the project retains the proposed single-holder policy; a general contribution license alone does not do that.

Use conventional commits and sign commits with your configured signing key. Do not add source comments. Snapshot changes belong with the behavior change and its review rationale.
