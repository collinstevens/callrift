# Source-only coverage

Status: open; tracked in the repository.

Source-only analysis uses runtime BCL references and a single compilation. The CleanArchitecture real-world cases depend on ASP.NET Core, MediatR, EF Core, FluentValidation, and generated project types. Unbound calls are marked `?` and reported in diagnostics. They must not be interpreted as resolved runtime targets. A binding can change from resolved to unresolved when callback type inference loses package information; that currently appears as removal/addition.

Serilog has conditional code and virtual visitor dispatch. The default mode does not evaluate project defines. Virtual overrides are followed, but receiver dataflow and branch feasibility remain unsupported; see the specific [receiver-flow issue](receiver-flow.md). External-only control-flow changes can appear as a body-change marker. Use `--externals` to expose BCL calls in such changes.

MSBuild mode supplies real references and defines. Framework convention dispatch still requires separate implementation. Snapshots deliberately retain these limitations so fixes receive review.
