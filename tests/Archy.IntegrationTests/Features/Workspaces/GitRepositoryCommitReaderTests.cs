using Archy.Features.Workspaces.LocateWorkspace;
using Archy.Features.Workspaces.ReadRepositoryCommit;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Workspaces;

public sealed class GitRepositoryCommitReaderTests
{
    [Fact]
    public async Task ReadHeadReturnsNoCommitForAnUnbornOrUnresolvableRepositoryHead()
    {
        using var fixture = TemporaryRepository.Create();
        var workspace = await fixture.LocateHandler.Handle(
            new LocateWorkspaceCommand(fixture.Root),
            CancellationToken.None);
        Assert.True(workspace.IsSuccess);

        var result = await new GitRepositoryCommitReader().ReadHeadAsync(
            workspace.Value,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ReadProvenanceCapturesCleanAndDirtyStatesWithoutRequiringACleanTree()
    {
        await using var repository = await GitRepositoryFixture.CreateAsync();
        await repository.WriteAsync("src/Tracked.cs", "namespace Sample; public sealed class Tracked { }");
        await repository.StageAllAsync();
        await repository.CommitAsync("initial");
        var workspace = new LocatedWorkspace(repository.Root, Path.Combine(repository.Root, ".git"), IsLinkedWorktree: false);
        var reader = new GitRepositoryCommitReader();

        var clean = await reader.ReadAsync(workspace, CancellationToken.None);

        Assert.True(clean.IsSuccess, clean.IsSuccess ? string.Empty : clean.Problem!.Message);
        Assert.NotNull(clean.Value.HeadCommit);
        Assert.Equal(RepositoryWorktreeState.Clean, clean.Value.WorktreeState);
        Assert.Empty(clean.Value.ChangedPaths);

        await repository.WriteAsync("src/Tracked.cs", "namespace Sample; public sealed class Changed { }");
        await repository.WriteAsync("src/Untracked.cs", "namespace Sample; public sealed class Untracked { }");
        var dirty = await reader.ReadAsync(workspace, CancellationToken.None);

        Assert.True(dirty.IsSuccess, dirty.IsSuccess ? string.Empty : dirty.Problem!.Message);
        Assert.Equal(RepositoryWorktreeState.Dirty, dirty.Value.WorktreeState);
        Assert.Equal(["src/Tracked.cs", "src/Untracked.cs"], dirty.Value.ChangedPaths);
    }
}
