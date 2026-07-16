using Archy.Features.Health.HealthSnapshots;
using Archy.Features.Health.ReadHealthComponentPages;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Health.ReadHealthComponentPages;

public sealed class HealthComponentPageReaderTests
{
    [Fact]
    public async Task PagesComponentsAndRetainsSnapshotMetadata()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("node:health", "hash")]);
        var stored = await new HealthSnapshotRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).RecordAsync(initialized.Value.StateLocation, new HealthSnapshotFact(revision, "v1", 75, [new HealthMetricComponentFact("alpha", 1, 1, 1, "{}"), new HealthMetricComponentFact("beta", 2, 1, 2, "{}")], []), CancellationToken.None);
        Assert.True(stored.IsSuccess);

        var page = await new HealthComponentPageReader(new WorkspaceLockManager(TimeProvider.System)).ReadAsync(initialized.Value.StateLocation, stored.Value.HealthSnapshotId, 1, 1, CancellationToken.None);

        Assert.True(page.IsSuccess, page.IsSuccess ? string.Empty : page.Problem!.Message);
        Assert.Equal(revision, page.Value.GraphRevision);
        Assert.Equal(75, page.Value.Score);
        Assert.Equal(2, page.Value.TotalCount);
        Assert.Equal("beta", Assert.Single(page.Value.Items).ComponentKey);
    }

    [Fact]
    public async Task RejectsUnsafeHealthPageSize()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);

        var result = await new HealthComponentPageReader(new WorkspaceLockManager(TimeProvider.System)).ReadAsync(initialized.Value.StateLocation, "snapshot", 0, 101, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
    }
}
