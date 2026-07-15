using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Workspaces.ResolveWorkspaceState;

public sealed class ResolveWorkspaceStateHandler(
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader,
    IWorkspaceStateLayout stateLayout)
    : IRequestHandler<ResolveWorkspaceStateQuery, Result<WorkspaceStateLocation>>
{
    public async ValueTask<Result<WorkspaceStateLocation>> Handle(
        ResolveWorkspaceStateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var workspace = workspaceLocator.Locate(query.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceStateLocation>(workspace.Problem!);
        }

        var configuration = await configurationLoader.LoadAsync(
            workspace.Value,
            query.ExplicitConfigurationPath,
            query.StateRootOverride,
            cancellationToken);
        if (!configuration.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceStateLocation>(configuration.Problem!);
        }

        return stateLayout.Resolve(workspace.Value, configuration.Value.StateRoot);
    }
}
