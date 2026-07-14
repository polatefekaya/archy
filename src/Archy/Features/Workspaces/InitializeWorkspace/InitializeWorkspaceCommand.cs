using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed record InitializeWorkspaceCommand(
    string StartPath,
    string? StateRoot,
    string? ExplicitConfigurationPath)
    : IRequest<Result<InitializedWorkspace>>;
