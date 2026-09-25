# Source-only coverage

Status: open; tracked locally until a public repository is authorized.

Source-only analysis uses runtime BCL references and a single compilation. The CleanArchitecture real-world cases depend on ASP.NET Core, MediatR, EF Core, FluentValidation, and generated project types. Unbound calls are marked `?` and reported in diagnostics. They must not be interpreted as resolved runtime targets. A binding can change from resolved to unresolved when callback type inference loses package information; that currently appears as removal/addition.

Serilog has conditional code and virtual visitor dispatch. The default mode does not evaluate project defines or follow non-abstract virtual overrides. External-only control-flow changes can appear as a body-change marker. Use `--externals` to expose BCL calls in such changes.

M3 will supply real references and defines. Following virtual dispatch and framework convention dispatch requires separate implementation. Snapshots deliberately retain these limitations so fixes receive review.
