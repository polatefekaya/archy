using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Duplicates.IndexEmbeddings;
using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Memory.AuthorizeAiSourceSharing;
using Archy.Features.Memory.DetermineImportantNodes;
using Archy.Features.Memory.GovernModelRequests;
using Archy.Features.Memory.ModelProviders.Deterministic;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.IntegrationTests.TestInfrastructure;
using System.Text.Json;

namespace Archy.IntegrationTests.Features.Duplicates.IndexEmbeddings;

public sealed class EmbeddingIndexWorkflowTests
{
    [Fact]
    public async Task IndexesThenReusesUnchangedPublicMethodContent()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        await WriteSourceAsync(fixture.Repository.Root, "src/Sessions.cs", "Create");
        var node = Node("method:Create", "src/Sessions.cs");
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node], symbols: [PublicSymbol(node.StableId)]);
        var configuration = Configuration();
        var reader = new EmbeddingIndexSourceReader(new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)), new ImportantNodeEligibilityPolicy(), new CSharpEmbeddingChunkSelector());
        var plan = await reader.ReadAsync(initialized.Value.StateLocation, fixture.Repository.Root, configuration, 10, CancellationToken.None);
        Assert.True(plan.IsSuccess); Assert.Equal(revision, plan.Value!.GraphRevision); Assert.Single(plan.Value.Chunks);
        var cache = new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var indexer = new EmbeddingIndexer(new RepositoryAiConsentPolicy(), new EmbeddingCacheResolver(cache), new ModelRequestGovernor(TimeProvider.System));
        var provider = new DeterministicModelProvider(providerId: "openai");
        var first = await indexer.IndexAsync(new(initialized.Value.StateLocation, revision, configuration, "test-model", plan.Value.Chunks, provider), CancellationToken.None);
        var second = await indexer.IndexAsync(new(initialized.Value.StateLocation, revision, configuration, "test-model", plan.Value.Chunks, provider), CancellationToken.None);
        Assert.True(first.IsSuccess); Assert.Equal(1, first.Value!.Generated);
        Assert.True(second.IsSuccess); Assert.Equal(0, second.Value!.Generated); Assert.Equal(1, second.Value.CacheHits);
        var cached = await cache.ListByModelAsync(initialized.Value.StateLocation, "test-model", 10, CancellationToken.None);
        var entry = Assert.Single(cached.Value!); Assert.Equal(revision, entry.CreatedGraphRevision); Assert.Equal(plan.Value.Chunks[0].ContentHash, entry.Key.ContentHash);

        await WriteSourceAsync(fixture.Repository.Root, "src/Sessions.cs", "CreateChanged");
        var changedNode = Node("method:Create", "src/Sessions.cs") with { ContentHash = new string('c', 64) };
        var changedRevision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [changedNode], symbols: [PublicSymbol(changedNode.StableId)]);
        var changedPlan = await reader.ReadAsync(initialized.Value.StateLocation, fixture.Repository.Root, configuration, 10, CancellationToken.None);
        var changed = await indexer.IndexAsync(new(initialized.Value.StateLocation, changedRevision, configuration, "test-model", changedPlan.Value!.Chunks, provider), CancellationToken.None);
        Assert.True(changed.IsSuccess); Assert.Equal(1, changed.Value!.Generated);
        var entries = await cache.ListByModelAsync(initialized.Value.StateLocation, "test-model", 10, CancellationToken.None);
        Assert.Equal(2, entries.Value!.Count);
    }

    [Fact]
    public async Task StatusReturnsStatisticsWithoutVectorContents()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var node = Node("method:Status", "src/Status.cs"); var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node]);
        var cache = new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        Assert.True((await cache.StoreAsync(initialized.Value.StateLocation, new(new(node.StableId, "test-model", new string('b', 64)), [1f, 0f], revision, DateTimeOffset.UtcNow), CancellationToken.None)).IsSuccess);
        var status = await new EmbeddingIndexStatusHandler(cache, new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System))).Handle(new(initialized.Value.StateLocation, Configuration(), "test-model"), CancellationToken.None);
        Assert.True(status.IsSuccess); Assert.Equal(1, status.Value!.CachedVectorCount); Assert.Equal(1, status.Value.Dimensions[2]);
    }

    [Fact]
    public async Task FindSimilarUsesCachedEvidenceProducedByTheIndexer()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var source = Node("method:CreateSession", "src/Sessions.cs"); var target = Node("method:CreateArchitectureSession", "src/Sessions.cs");
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [source, target]);
        var cache = new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        var indexer = new EmbeddingIndexer(new RepositoryAiConsentPolicy(), new EmbeddingCacheResolver(cache), new ModelRequestGovernor(TimeProvider.System));
        var chunks = new[] { new EmbeddingChunk(source.StableId, source.FilePath!, "create an architecture session", new string('d', 64)), new EmbeddingChunk(target.StableId, target.FilePath!, "create an architecture session with events", new string('e', 64)) };
        var indexed = await indexer.IndexAsync(new(initialized.Value.StateLocation, revision, Configuration(), "test-model", chunks, new DeterministicModelProvider(providerId: "openai")), CancellationToken.None);
        Assert.True(indexed.IsSuccess); Assert.Equal(2, indexed.Value!.Generated);
        using var arguments = JsonDocument.Parse("""{"query":"create an architecture session","sourceStableId":"method:CreateSession","embeddingModel":"test-model"}""");
        var result = await new FindSimilarMcpTool().ExecuteAsync(new("find_similar", arguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);
        Assert.True(result.IsSuccess); using var response = JsonDocument.Parse(result.ResultJson!);
        Assert.Equal("cached", response.RootElement.GetProperty("structuredContent").GetProperty("embeddingEvidence").GetString());
    }

    private static ArchyConfiguration Configuration() => ArchyConfiguration.Default with { Model = ArchyConfiguration.Default.Model with { Provider = "openai", EmbeddingModel = "test-model" }, Memory = ArchyConfiguration.Default.Memory with { SourceSharing = AiSourceSharingMode.SummariesAndEmbeddings } };
    private static GraphNodeFact Node(string id, string path) => new(id, "method", id, id, path, 4, 7, "test", 1, "{}", new string('a', 64));
    private static GraphSymbolFact PublicSymbol(string id) => new($"symbol:{id}", id, id, "public", "Create()", "[]", "{}", "hash");
    private static async Task WriteSourceAsync(string root, string relativePath, string method) { var path = Path.Combine(root, relativePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "namespace Sample;\npublic sealed class Sessions\n{\n    public void " + method + "()\n    {\n        _ = 1;\n    }\n}\n"); }
}
