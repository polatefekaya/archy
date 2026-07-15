using Archy.Features.Analysis.InventorySources;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.InventorySources;

public sealed class SourceInventoryFailureAndStateTests
{
    [Fact]
    public async Task InventoryDoesNotLoadGitIgnoreRulesFromManagedStateOrGeneratedDirectories()
    {
        using var fixture = await SourceInventoryFixture.CreateAsync(stateRootWithinRepository: true);
        await fixture.WriteRepositoryFileAsync("src/InventoryTarget.cs", "namespace Sample; public sealed class InventoryTarget { }");
        await fixture.WriteRepositoryFileAsync("bin/.gitignore", "..");
        await fixture.WriteRepositoryFileAsync("obj/.gitignore", "..");
        await fixture.WriteStateFileAsync(".gitignore", "..");

        var result = await fixture.SynchronizeAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains(
            result.Value.Files,
            static file => file.RepositoryRelativePath == "src/InventoryTarget.cs");
        var statePath = Path.GetRelativePath(fixture.RepositoryRoot, fixture.Location.StateDirectory)
            .Replace(Path.DirectorySeparatorChar, '/');
        Assert.Contains(
            result.Value.Exclusions,
            exclusion => exclusion is { RepositoryRelativePath: var path, Reason: SourcePathExclusionReason.ArchyState } &&
                         string.Equals(path, statePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task InventoryRejectsAnUninitializedWorkspaceBeforeCreatingCacheState()
    {
        using var workspace = WorkspaceStateFixture.Create();
        var located = await workspace.Repository.LocateHandler.Handle(
            new LocateWorkspaceCommand(workspace.Repository.Root),
            CancellationToken.None);
        Assert.True(located.IsSuccess);
        var location = new WorkspaceStateLayout().Resolve(located.Value, workspace.StateRoot);
        Assert.True(location.IsSuccess);

        var result = await new RepositorySourceInventory(
            TimeProvider.System,
            new WorkspaceLockManager(TimeProvider.System)).SynchronizeAsync(
            location.Value,
            workspace.Repository.Root,
            ArchyConfiguration.Default,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Problem!.Code);
        Assert.False(File.Exists(location.Value.DatabasePath));
    }

    [Fact]
    public async Task InventoryReportsAnUnsupportedCachedLanguageAsAStorageProblem()
    {
        using var fixture = await SourceInventoryFixture.CreateAsync();
        await fixture.WriteRepositoryFileAsync("src/App.cs", "namespace Sample; public sealed class App { }");
        var initial = await fixture.SynchronizeAsync();
        Assert.True(initial.IsSuccess);
        await SqliteTestDatabase.ExecuteAsync(
            fixture.Location.DatabasePath,
            "UPDATE source_inventory_files SET language = 'unsupported';");

        var result = await fixture.SynchronizeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("storage_error", result.Problem!.Code);
    }
}
