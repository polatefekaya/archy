using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Storage.InitializeWorkspaceDatabase;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed class InitializeWorkspaceHandler(
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader,
    IWorkspaceStateLayout stateLayout,
    IWorkspaceLockManager workspaceLockManager,
    IWorkspaceManifestStore manifestStore,
    IWorkspaceDatabaseInitializer databaseInitializer)
    : IRequestHandler<InitializeWorkspaceCommand, Result<InitializedWorkspace>>
{
    public async ValueTask<Result<InitializedWorkspace>> Handle(
        InitializeWorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = workspaceLocator.Locate(command.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<InitializedWorkspace>(workspace.Problem!);
        }

        var configuration = await configurationLoader.LoadAsync(
            workspace.Value,
            command.ExplicitConfigurationPath,
            command.StateRoot,
            cancellationToken);
        if (!configuration.IsSuccess)
        {
            return ResultFactory.Failure<InitializedWorkspace>(configuration.Problem!);
        }

        var location = stateLayout.Resolve(workspace.Value, configuration.Value.StateRoot);
        if (!location.IsSuccess)
        {
            return ResultFactory.Failure<InitializedWorkspace>(location.Problem!);
        }

        var workspaceLock = await workspaceLockManager.AcquireAsync(
            location.Value,
            WorkspaceLockMode.Write,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!workspaceLock.IsSuccess)
        {
            return ResultFactory.Failure<InitializedWorkspace>(workspaceLock.Problem!);
        }

        using var lease = workspaceLock.Value;
        var storedManifest = await manifestStore.LoadOrCreateAsync(
            location.Value,
            workspace.Value,
            cancellationToken);
        if (!storedManifest.IsSuccess)
        {
            return ResultFactory.Failure<InitializedWorkspace>(storedManifest.Problem!);
        }

        var database = await databaseInitializer.InitializeAsync(
            location.Value,
            storedManifest.Value.Manifest,
            ConfigurationFingerprint.Calculate(configuration.Value.Configuration),
            cancellationToken);
        if (!database.IsSuccess)
        {
            return ResultFactory.Failure<InitializedWorkspace>(database.Problem!);
        }

        return ResultFactory.Success(
            new InitializedWorkspace(
                location.Value,
                storedManifest.Value.Manifest,
                storedManifest.Value.WasCreated));
    }
}
