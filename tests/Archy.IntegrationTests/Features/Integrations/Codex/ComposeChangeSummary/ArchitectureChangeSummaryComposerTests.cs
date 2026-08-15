using Archy.Features.Integrations.Codex.ComposeChangeSummary;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Codex.ComposeChangeSummary;

public sealed class ArchitectureChangeSummaryComposerTests
{
    [Fact]
    public async Task ComposesDeterministicAddRemoveAndChangeEvidenceBetweenRevisions()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var original = GraphRevisionTestBuilder.Node("node:original", "aaaaaaaaaaaaaaaa");
        var changed = GraphRevisionTestBuilder.Node("node:changed", "bbbbbbbbbbbbbbbb");
        var first = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [original, changed]);
        var added = GraphRevisionTestBuilder.Node("node:added", "cccccccccccccccc");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [changed with { ContentHash = "dddddddddddddddd" }, added]);
        var composer = new ArchitectureChangeSummaryComposer(new WorkspaceLockManager(TimeProvider.System));

        var result = await composer.ComposeAsync(initialized.Value.StateLocation, first, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.False(result.Value!.Abstained); Assert.Equal("node:added", Assert.Single(result.Value.AddedNodes).StableId);
        Assert.Equal("node:original", Assert.Single(result.Value.RemovedNodes).StableId);
        Assert.Equal("node:changed", Assert.Single(result.Value.ChangedNodes).StableId);
        Assert.Contains("Graph revision", result.Value.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbstainsWhenBaseRevisionIsNotTrustworthy()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:one", "aaaaaaaaaaaaaaaa")]);
        var result = await new ArchitectureChangeSummaryComposer(new WorkspaceLockManager(TimeProvider.System)).ComposeAsync(initialized.Value.StateLocation, 99, CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.True(result.Value!.Abstained); Assert.Contains("unavailable", result.Value.AbstentionReason!, StringComparison.Ordinal);
    }
}
