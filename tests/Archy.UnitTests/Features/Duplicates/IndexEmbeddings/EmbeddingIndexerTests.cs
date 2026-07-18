using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Duplicates.IndexEmbeddings;
using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Memory.AuthorizeAiSourceSharing;
using Archy.Features.Memory.GovernModelRequests;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.UnitTests.Features.Duplicates.IndexEmbeddings;

public sealed class EmbeddingIndexerTests
{
    [Fact]
    public async Task RejectsDisabledConsentBeforeResolverUse()
    {
        var resolver = new FakeResolver();
        var result = await CreateIndexer(resolver).IndexAsync(Request(memory: ArchyConfiguration.Default.Memory with { SourceSharing = AiSourceSharingMode.Disabled }), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task RejectsMissingModelBeforeResolverUse()
    {
        var resolver = new FakeResolver();
        var result = await CreateIndexer(resolver).IndexAsync(Request(model: null), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task RejectsMismatchedProviderBeforeResolverUse()
    {
        var resolver = new FakeResolver();
        var configuration = Config() with { Model = Config().Model with { Provider = "other" } };
        var result = await CreateIndexer(resolver).IndexAsync(Request(configuration: configuration), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task GovernorDenialPreventsResolverUse()
    {
        var resolver = new FakeResolver(); var governor = new FakeGovernor { Granted = false };
        var result = await CreateIndexer(resolver, governor).IndexAsync(Request(), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task CacheHitOnlyResolutionReportsNoGeneration()
    {
        var resolver = new FakeResolver { Result = ModelProviderResults.Success(new EmbeddingCacheResolution("model", new Dictionary<string, IReadOnlyList<float>>(), 1, 0, new(0, 0, 0, 0))) };
        var result = await CreateIndexer(resolver).IndexAsync(Request(), CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Equal(0, result.Value!.Generated); Assert.Equal(1, result.Value.CacheHits);
    }

    [Fact]
    public async Task CompletesLeaseAfterProviderFailure()
    {
        var governor = new FakeGovernor(); var resolver = new FakeResolver { Result = ModelProviderResults.Fail<EmbeddingCacheResolution>(new(ModelProviderFailureKind.RateLimited, "slow down", true, TimeSpan.FromSeconds(1))) };
        var result = await CreateIndexer(resolver, governor).IndexAsync(Request(), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal(1, governor.Completed); Assert.Equal(1, governor.Cooldowns);
    }

    [Fact]
    public async Task EnforcesConfiguredChunkBound()
    {
        var resolver = new FakeResolver();
        var configuration = Config(maxRequests: 1);
        var request = Request(configuration: configuration, chunks: [Chunk("method:a"), Chunk("method:b")]);
        await CreateIndexer(resolver).IndexAsync(request, CancellationToken.None);
        Assert.Equal(1, resolver.LastChunkCount);
    }

    private static EmbeddingIndexer CreateIndexer(FakeResolver resolver, FakeGovernor? governor = null) => new(new RepositoryAiConsentPolicy(), resolver, governor ?? new FakeGovernor());
    private static EmbeddingIndexRequest Request(MemorySelectionConfiguration? memory = null, string? model = "model", ArchyConfiguration? configuration = null, IReadOnlyList<EmbeddingChunk>? chunks = null)
    {
        configuration ??= Config(); configuration = configuration with { Memory = memory ?? configuration.Memory, Model = configuration.Model with { EmbeddingModel = model } };
        return new(new("workspace", "/tmp", "/tmp/manifest", "/tmp/lock", "/tmp/db"), 1, configuration, model ?? string.Empty, chunks ?? [Chunk("method:a")], new FakeProvider());
    }
    private static ArchyConfiguration Config(int maxRequests = 30) => ArchyConfiguration.Default with { Model = ArchyConfiguration.Default.Model with { Provider = "test", EmbeddingModel = "model", MaxRequestsPerRun = maxRequests }, Memory = ArchyConfiguration.Default.Memory with { SourceSharing = AiSourceSharingMode.SummariesAndEmbeddings } };
    private static EmbeddingChunk Chunk(string id) => new(id, "src/Test.cs", "content", new string('a', 64));

    private sealed class FakeResolver : IEmbeddingCacheResolver
    {
        public int Calls { get; private set; } public int LastChunkCount { get; private set; }
        public ModelProviderResult<EmbeddingCacheResolution> Result { get; set; } = ModelProviderResults.Success<EmbeddingCacheResolution>(new("model", new Dictionary<string, IReadOnlyList<float>>(), 0, 1, new(1, 0, 0, 1)));
        public ValueTask<ModelProviderResult<EmbeddingCacheResolution>> ResolveAsync(WorkspaceStateLocation location, long graphRevision, string modelId, IReadOnlyList<EmbeddingChunk> chunks, IModelProvider provider, CancellationToken cancellationToken) { Calls++; LastChunkCount = chunks.Count; return ValueTask.FromResult(Result); }
    }
    private sealed class FakeGovernor : IModelRequestGovernor
    {
        public bool Granted { get; set; } = true; public int Completed { get; private set; } public int Cooldowns { get; private set; }
        public ModelGovernorDecision TryReserve(string runId, string requestId, ModelRequestEstimate estimate, ModelConfiguration configuration) => Granted ? new(new(runId, requestId, estimate, DateTimeOffset.UtcNow), ModelGovernorDenial.None, null) : new(null, ModelGovernorDenial.RequestQuotaExhausted, null);
        public void Complete(ModelRequestLease lease) => Completed++; public void ApplyRateLimitCooldown(TimeSpan? retryAfter, ModelConfiguration configuration) => Cooldowns++;
    }
    private sealed class FakeProvider : IModelProvider
    {
        public ModelProviderDescriptor Descriptor { get; } = new("test", false, true);
        public ValueTask<ModelProviderResult<StructuredSummaryResponse>> GenerateSummaryAsync(SummaryGenerationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ModelProviderResult<EmbeddingGenerationResponse>> GenerateEmbeddingsAsync(EmbeddingGenerationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
