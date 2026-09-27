using Callrift.MSBuild;

namespace Callrift.FixtureWorker;

internal sealed record FixtureWorkerRequest(WorkspaceRequest Workspace, string? PreparedShape);
