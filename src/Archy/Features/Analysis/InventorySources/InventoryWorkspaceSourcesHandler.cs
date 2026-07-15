using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Analysis.InventorySources;

public sealed class InventoryWorkspaceSourcesHandler(
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader,
    IWorkspaceStateLayout stateLayout,
    IRepositorySourceInventory sourceInventory)
    : IRequestHandler<InventoryWorkspaceSourcesCommand, Result<SourceInventory>>
{
    public async ValueTask<Result<SourceInventory>> Handle(
        InventoryWorkspaceSourcesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var workspace = workspaceLocator.Locate(command.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<SourceInventory>(workspace.Problem!);
        }

        var configuration = await configurationLoader.LoadAsync(
            workspace.Value,
            command.ExplicitConfigurationPath,
            command.StateRootOverride,
            cancellationToken);
        if (!configuration.IsSuccess)
        {
            return ResultFactory.Failure<SourceInventory>(configuration.Problem!);
        }

        var location = stateLayout.Resolve(workspace.Value, configuration.Value.StateRoot);
        if (!location.IsSuccess)
        {
            return ResultFactory.Failure<SourceInventory>(location.Problem!);
        }

        return await sourceInventory.SynchronizeAsync(
            location.Value,
            workspace.Value.RepositoryRoot,
            configuration.Value.Configuration,
            cancellationToken);
    }
}
