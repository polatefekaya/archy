using Archy.Features.Memory.ReadSummaryPages;
using Archy.Features.Memory.Summaries;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Memory.ReadSummaryPages;

public sealed class SummaryVersionPageReaderTests
{
    [Fact]
    public async Task ReadsChronologicalBoundedVersionsWithoutMaterializingTheWholeHistory()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:summary", "hash")]);
        var summaries = new SummaryRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        for (var version = 1; version <= 3; version++)
        {
            var appended = await summaries.AppendAsync(initialized.Value.StateLocation, Fact(revision, $"Summary {version}"), CancellationToken.None);
            Assert.True(appended.IsSuccess);
        }

        var page = await new SummaryVersionPageReader(new WorkspaceLockManager(TimeProvider.System)).ReadAsync(initialized.Value.StateLocation, "summary:node", offset: 1, limit: 1, CancellationToken.None);

        Assert.True(page.IsSuccess, page.IsSuccess ? string.Empty : page.Problem!.Message);
        Assert.Equal(3, page.Value.TotalCount);
        var item = Assert.Single(page.Value.Items);
        Assert.Equal(2, item.VersionNumber);
        Assert.Equal("Summary 2", item.SummaryText);
    }

    [Fact]
    public async Task RejectsUnsafePagingBeforeOpeningSummaryStorage()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var reader = new SummaryVersionPageReader(new WorkspaceLockManager(TimeProvider.System));

        var rejected = await reader.ReadAsync(initialized.Value.StateLocation, "summary", 0, 51, CancellationToken.None);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("validation", rejected.Problem!.Code);
    }

    private static SummaryVersionFact Fact(long revision, string text) => new(
        "summary:node", "graph_node", "node:summary", revision, null, text, "diff", "fixture", "none", "{}", SummaryStaleness.Fresh);
}
