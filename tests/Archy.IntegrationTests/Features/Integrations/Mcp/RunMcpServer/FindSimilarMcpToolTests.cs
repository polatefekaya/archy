using System.Text.Json;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class FindSimilarMcpToolTests
{
    [Fact]
    public async Task EmitsCachedEmbeddingEvidenceForCompatiblePersistedVectors()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var source = GraphRevisionTestBuilder.Node("method:CreateSession", "aaaaaaaaaaaaaaaa");
        var target = GraphRevisionTestBuilder.Node("method:CreateArchitectureSession", "bbbbbbbbbbbbbbbb");
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [source, target]);
        var cache = new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        Assert.True((await cache.StoreAsync(initialized.Value.StateLocation, new(new(source.StableId, "test-model", source.ContentHash), [1f, 0f], revision, DateTimeOffset.UtcNow), CancellationToken.None)).IsSuccess);
        Assert.True((await cache.StoreAsync(initialized.Value.StateLocation, new(new(target.StableId, "test-model", target.ContentHash), [.9f, .1f], revision, DateTimeOffset.UtcNow), CancellationToken.None)).IsSuccess);
        using var arguments = JsonDocument.Parse("""{"query":"create session","sourceStableId":"method:CreateSession","embeddingModel":"test-model"}""");
        var result = await new FindSimilarMcpTool().ExecuteAsync(new("find_similar", arguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        using var response = JsonDocument.Parse(result.ResultJson!);
        Assert.Equal("cached", response.RootElement.GetProperty("structuredContent").GetProperty("embeddingEvidence").GetString());
        var candidate = response.RootElement.GetProperty("structuredContent").GetProperty("candidates").EnumerateArray().First();
        Assert.Contains(candidate.GetProperty("evidence").EnumerateArray(), evidence => evidence.GetProperty("kind").GetString() == "embedding");
    }
}
