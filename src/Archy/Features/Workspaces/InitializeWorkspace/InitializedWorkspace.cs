namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed record InitializedWorkspace(
    WorkspaceStateLocation StateLocation,
    WorkspaceManifest Manifest,
    bool WasCreated);
