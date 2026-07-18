using System.Text.Json;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Duplicates.IndexEmbeddings;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Memory.AuthorizeAiSourceSharing;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Memory.ModelProviders.Deterministic;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

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

    [Fact]
    public async Task AcceptsRepositoryFileAndCodeSnippetInputsWithoutAStableId()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        Directory.CreateDirectory(Path.Combine(fixture.Repository.Root, "src"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository.Root, "src", "Sessions.cs"), "public sealed class Sessions { public void CreateSession() { } }");
        var node = new GraphNodeFact("type:Sessions", "class", "Sessions", "CreateSession", "src/Sessions.cs", 1, 1, "test", 1, "{}", new string('a', 64));
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [node]);

        using var fileArguments = JsonDocument.Parse("""{"sourceFilePath":"src/Sessions.cs"}""");
        using var codeArguments = JsonDocument.Parse("""{"sourceCode":"public void CreateSession() { }"}""");
        var tool = new FindSimilarMcpTool();

        var fromFile = await tool.ExecuteAsync(new("find_similar", fileArguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);
        var fromCode = await tool.ExecuteAsync(new("find_similar", codeArguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);

        Assert.True(fromFile.IsSuccess);
        Assert.True(fromCode.IsSuccess);
    }

    [Fact]
    public async Task GeneratesAConsentedSemanticVectorForCodeInputAndComparesItWithCachedCorpusVectors()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var source = new GraphNodeFact("type:Sessions", "class", "Sample.Sessions", "CreateSession", "src/Sessions.cs", 1, 1, "test", 1, "{}", new string('a', 64));
        var target = new GraphNodeFact("type:ArchitectureSessions", "class", "Sample.ArchitectureSessions", "CreateArchitectureSession", "src/ArchitectureSessions.cs", 1, 1, "test", 1, "{}", new string('b', 64));
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [source, target]);
        var cache = new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));
        Assert.True((await cache.StoreAsync(initialized.Value.StateLocation, new(new(target.StableId, "test-model", target.ContentHash), [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f], revision, DateTimeOffset.UtcNow), CancellationToken.None)).IsSuccess);
        var configuration = ArchyConfiguration.Default with
        {
            Model = ArchyConfiguration.Default.Model with { Provider = "test", EmbeddingModel = "test-model" },
            Memory = ArchyConfiguration.Default.Memory with { SourceSharing = AiSourceSharingMode.SummariesAndEmbeddings },
        };
        using var arguments = JsonDocument.Parse("""{"sourceCode":"public void CreateSession() { }","limit":5}""");
        var tool = new FindSimilarMcpTool(
            cacheRepository: cache,
            cacheResolver: new EmbeddingCacheResolver(cache),
            providerResolver: new DeterministicProviderResolver(),
            consentPolicy: new RepositoryAiConsentPolicy(),
            configurationOverride: configuration);

        var result = await tool.ExecuteAsync(new("find_similar", arguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var response = JsonDocument.Parse(result.ResultJson!);
        var content = response.RootElement.GetProperty("structuredContent");
        Assert.Equal("generated", content.GetProperty("embeddingEvidence").GetString());
        Assert.Contains(content.GetProperty("candidates").EnumerateArray(), candidate => candidate.GetProperty("evidence").EnumerateArray().Any(evidence => evidence.GetProperty("kind").GetString() == "embedding"));
        var cached = await cache.ListByModelAsync(initialized.Value.StateLocation, "test-model", 10, CancellationToken.None);
        Assert.True(cached.IsSuccess);
        Assert.Equal(2, cached.Value!.Count);
    }

    private sealed class DeterministicProviderResolver : IEmbeddingModelProviderResolver
    {
        public Result<IModelProvider> Resolve(string configuredProvider) => ResultFactory.Success<IModelProvider>(new DeterministicModelProvider(providerId: configuredProvider));
    }
}
