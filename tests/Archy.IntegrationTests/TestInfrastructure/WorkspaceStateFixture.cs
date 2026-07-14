using Archy.Features.Storage.InitializeWorkspaceDatabase;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.IntegrationTests.TestInfrastructure;

internal sealed class WorkspaceStateFixture : IDisposable
{
    private WorkspaceStateFixture(TemporaryRepository repository, string stateRoot)
    {
        Repository = repository;
        StateRoot = stateRoot;
        InitializeHandler = new InitializeWorkspaceHandler(
            new Archy.Features.Workspaces.LocateWorkspace.WorkspaceLocator(),
            TestConfigurationFactory.CreateLoader(),
            new WorkspaceStateLayout(),
            new WorkspaceLockManager(TimeProvider.System),
            new WorkspaceManifestStore(TimeProvider.System),
            new WorkspaceDatabaseInitializer(TimeProvider.System));
    }

    public TemporaryRepository Repository { get; }

    public string StateRoot { get; }

    public InitializeWorkspaceHandler InitializeHandler { get; }

    public static WorkspaceStateFixture Create()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"archy-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stateRoot);
        return new WorkspaceStateFixture(TemporaryRepository.Create(), stateRoot);
    }

    public ValueTask<Archy.SharedKernel.Primitives.Result<InitializedWorkspace>> InitializeAsync() =>
        InitializeHandler.Handle(
            new InitializeWorkspaceCommand(Repository.Root, StateRoot, ExplicitConfigurationPath: null),
            CancellationToken.None);

    public void Dispose()
    {
        Repository.Dispose();
        if (Directory.Exists(StateRoot))
        {
            Directory.Delete(StateRoot, recursive: true);
        }
    }
}
