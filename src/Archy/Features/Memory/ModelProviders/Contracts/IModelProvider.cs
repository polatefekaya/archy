namespace Archy.Features.Memory.ModelProviders.Contracts;

/// <summary>Provider-neutral, cancellation-aware boundary for structured summaries and embeddings.</summary>
public interface IModelProvider
{
    ModelProviderDescriptor Descriptor { get; }

    ValueTask<ModelProviderResult<StructuredSummaryResponse>> GenerateSummaryAsync(
        SummaryGenerationRequest request,
        CancellationToken cancellationToken);

    ValueTask<ModelProviderResult<EmbeddingGenerationResponse>> GenerateEmbeddingsAsync(
        EmbeddingGenerationRequest request,
        CancellationToken cancellationToken);
}
