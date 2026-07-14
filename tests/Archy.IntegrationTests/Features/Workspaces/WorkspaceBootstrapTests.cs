using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Workspaces;

public sealed class WorkspaceBootstrapTests
{
    [Fact]
    public async Task InitializeCreatesExternalStateAndIsIdempotent()
    {
        using var fixture = WorkspaceStateFixture.Create();

        var first = await fixture.InitializeAsync();
        var second = await fixture.InitializeAsync();

        Assert.True(first.IsSuccess);
        Assert.True(first.Value.WasCreated);
        Assert.True(File.Exists(first.Value.StateLocation.ManifestPath));
        Assert.True(File.Exists(first.Value.StateLocation.DatabasePath));
        Assert.True(second.IsSuccess);
        Assert.False(second.Value.WasCreated);
        Assert.Equal(first.Value.StateLocation.WorkspaceId, second.Value.StateLocation.WorkspaceId);

        await SqliteAssertions.AssertBootstrapAsync(
            first.Value.StateLocation.DatabasePath,
            first.Value.StateLocation.WorkspaceId);
    }
}
