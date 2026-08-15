using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Integrations.Codex.AdvisePostEditReuse;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Codex.AdvisePostEditReuse;

public sealed class PostEditReuseAdvisorTests
{
    [Fact]
    public async Task ReturnsOnlyCrossFilePersistedCandidatesAndLabelsTheGraphAsPotentiallyStale()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var changed = GraphRevisionTestBuilder.Node("node:changed", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "Sessions.CreateSession", FilePath = "src/Changed.cs" };
        var existing = GraphRevisionTestBuilder.Node("node:existing", "bbbbbbbbbbbbbbbb") with { DisplayName = "CreateSession", CanonicalKey = "Sessions.CreateSession", FilePath = "src/Existing.cs" };
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [changed, existing]);
        var advisor = new PostEditReuseAdvisor(new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)));

        var result = await advisor.AdviseAsync(initialized.Value.StateLocation, ["src/Changed.cs"], CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message); Assert.True(result.Value!.GraphMayBeStale);
        Assert.Equal(existing.StableId, Assert.Single(result.Value.Candidates).StableId); Assert.NotNull(result.Value.StrongestExplanation);
    }

    [Fact]
    public async Task AbstainsForUnmappedChangedPaths()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:one", "aaaaaaaaaaaaaaaa")]);
        var result = await new PostEditReuseAdvisor(new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System))).AdviseAsync(initialized.Value.StateLocation, ["src/NewFile.cs"], CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Empty(result.Value!.Candidates); Assert.Contains("refresh", result.Value.AbstentionReason!, StringComparison.Ordinal);
    }
}
