# OrchardCore coverage limits

The workflow-startup and role-submission pairs cover a repository with more than 5,600 C# files and 245 projects. Source mode reads eligible repository C# as one compilation. Restored views select the Workflows or Roles module and its project references at net10.0. Historical global.json requests SDK 10.0.401 with latestMajor roll-forward.

Source views have 16,617 and 16,644 diagnostics respectively after the [constructor correction](../reviews/constructor-initialization.md). The additional 404 diagnostics per pair identify unbound implicit base constructors. Missing packages and project inputs leave controller, role-manager, notification, and workflow extension calls unresolved. Those nodes and diagnostics remain visible. Restored Roles views have no diagnostics. Restored Workflows views have six unresolved calls in Liquid ShapePagerTag's dynamic objectValue operations; these are outside the changed workflow methods.

The workflow pair changes both C# controller/helper code and a Razor warning badge. The role pair changes controller signatures and Razor submit-button values. The graph does not connect generated Razor execution to these changed members. Reviewed expectations distinguish C# call paths from view behavior. The deeper focused views expose guards and redirects but retain explicit depth limits.

These two pairs have independent source reviews and no generated node locations. Broader breadcrumb, media, and logout candidates remain unaccepted. The media investigation also motivated the separate [generic caller-context issue](generic-context.md). None of these snapshots establishes runtime framework invocation, DI resolution, or receiver values.
