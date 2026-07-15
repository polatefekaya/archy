using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Duplicates.EmbeddingCache;

public sealed class EmbeddingCacheResolverTests
{
    [Fact]
    public async Task ResolveAsyncReusesExactMethodModelAndContentAcrossUnchangedScans()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [GraphRevisionTestBuilder.Node("method:clock", "hash:v1")]);
        var provider = new CountingEmbeddingProvider();
        var resolver = new EmbeddingCacheResolver(new EmbeddingCacheRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)));
        var chunk = new EmbeddingChunk("method:clock", "src/Clock.cs", "signature: Clock", new string('A', 64));

        var first = await resolver.ResolveAsync(initialized.Value.StateLocation, revision, "embedding-test-v1", [chunk], provider, CancellationToken.None);
        var second = await resolver.ResolveAsync(initialized.Value.StateLocation, revision, "embedding-test-v1", [chunk], provider, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.GeneratedCount);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Value!.CacheHitCount);
        Assert.Equal(0, second.Value.GeneratedCount);
        Assert.Equal(1, provider.EmbeddingCallCount);
    }

    private sealed class CountingEmbeddingProvider : IModelProvider
    {
        public ModelProviderDescriptor Descriptor { get; } = new("test", SupportsStructuredSummaries: false, SupportsEmbeddings: true);

        public int EmbeddingCallCount { get; private set; }

        public ValueTask<ModelProviderResult<StructuredSummaryResponse>> GenerateSummaryAsync(SummaryGenerationRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(ModelProviderResults.Fail<StructuredSummaryResponse>(new ModelProviderFailure(ModelProviderFailureKind.InvalidRequest, "Not supported by this fixture.", false, null)));

        public ValueTask<ModelProviderResult<EmbeddingGenerationResponse>> GenerateEmbeddingsAsync(EmbeddingGenerationRequest request, CancellationToken cancellationToken)
        {
            EmbeddingCallCount++;
            var vectors = request.Inputs.Select(static input => new EmbeddingVector(input.InputId, [input.Content.Length, 1f])).ToArray();
            return ValueTask.FromResult(ModelProviderResults.Success(new EmbeddingGenerationResponse(request.RequestId, request.ModelId, vectors, new ModelUsage(2, 0, 0, 2), new ModelResponseMetadata("test-request", "{}"))));
        }
    }
}
