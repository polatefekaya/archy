using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;

namespace Archy.UnitTests.Features.Workspaces;

public sealed class WorkspaceStateLayoutTests
{
    [Fact]
    public void ResolveIsDeterministicForTheSameRepository()
    {
        var workspace = new LocatedWorkspace(
            "/tmp/archy-unit-repository",
            "/tmp/archy-unit-repository/.git",
            IsLinkedWorktree: false);
        var layout = new WorkspaceStateLayout();

        var first = layout.Resolve(workspace, requestedStateRoot: "/tmp/archy-unit-state");
        var second = layout.Resolve(workspace, requestedStateRoot: "/tmp/archy-unit-state");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.WorkspaceId, second.Value.WorkspaceId);
        Assert.Equal(first.Value.StateDirectory, second.Value.StateDirectory);
        Assert.Equal(
            Path.Combine("/tmp/archy-unit-state", first.Value.WorkspaceId),
            first.Value.StateDirectory);
    }

    [Fact]
    public void ResolveDefaultsToStateOutsideTheRepository()
    {
        var workspace = new LocatedWorkspace(
            "/tmp/archy-unit-repository",
            "/tmp/archy-unit-repository/.git",
            IsLinkedWorktree: false);

        var result = new WorkspaceStateLayout().Resolve(workspace, requestedStateRoot: null);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(workspace.RepositoryRoot, result.Value.StateDirectory, StringComparison.Ordinal);
    }
}
