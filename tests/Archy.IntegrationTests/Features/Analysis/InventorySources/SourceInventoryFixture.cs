using Archy.Features.Analysis.InventorySources;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.InventorySources;

internal sealed class SourceInventoryFixture : IDisposable
{
    private readonly WorkspaceStateFixture workspace;
    private readonly RepositorySourceInventory inventory;

    private SourceInventoryFixture(WorkspaceStateFixture workspace, WorkspaceStateLocation location)
    {
        this.workspace = workspace;
        Location = location;
        inventory = new RepositorySourceInventory(
            TimeProvider.System,
            new WorkspaceLockManager(TimeProvider.System));
    }

    public string RepositoryRoot => workspace.Repository.Root;

    public WorkspaceStateLocation Location { get; }

    public static async Task<SourceInventoryFixture> CreateAsync(bool stateRootWithinRepository = false)
    {
        var workspace = WorkspaceStateFixture.Create(stateRootWithinRepository);
        var initialized = await workspace.InitializeAsync();
        if (!initialized.IsSuccess)
        {
            workspace.Dispose();
            throw new InvalidOperationException(initialized.Problem!.Message);
        }

        return new SourceInventoryFixture(workspace, initialized.Value.StateLocation);
    }

    public ValueTask<Result<SourceInventory>> SynchronizeAsync(ArchyConfiguration? configuration = null) =>
        inventory.SynchronizeAsync(
            Location,
            RepositoryRoot,
            configuration ?? ArchyConfiguration.Default,
            CancellationToken.None);

    public async Task WriteRepositoryFileAsync(string repositoryRelativePath, string contents)
    {
        var fullPath = ToRepositoryPath(repositoryRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, contents);
    }

    public async Task WriteStateFileAsync(string stateRelativePath, string contents)
    {
        var fullPath = Path.Combine(
            Location.StateDirectory,
            stateRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, contents);
    }

    public void DeleteRepositoryFile(string repositoryRelativePath) =>
        File.Delete(ToRepositoryPath(repositoryRelativePath));

    public void Dispose() => workspace.Dispose();

    private string ToRepositoryPath(string repositoryRelativePath) =>
        Path.Combine(RepositoryRoot, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
}
