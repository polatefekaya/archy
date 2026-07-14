using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed class LoadEffectiveConfigurationHandler(
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader)
    : IRequestHandler<LoadEffectiveConfigurationQuery, Result<EffectiveConfiguration>>
{
    public async ValueTask<Result<EffectiveConfiguration>> Handle(
        LoadEffectiveConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = workspaceLocator.Locate(query.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(workspace.Problem!);
        }

        return await configurationLoader.LoadAsync(
            workspace.Value,
            query.ExplicitConfigurationPath,
            query.StateRootOverride,
            cancellationToken);
    }
}
