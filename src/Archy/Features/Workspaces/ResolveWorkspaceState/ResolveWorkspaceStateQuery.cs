using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Workspaces.ResolveWorkspaceState;

public sealed record ResolveWorkspaceStateQuery(
    string StartPath,
    string? ExplicitConfigurationPath,
    string? StateRootOverride)
    : IRequest<Result<WorkspaceStateLocation>>;
