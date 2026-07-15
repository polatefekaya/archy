namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record EmbeddingGenerationResponse(
    string RequestId,
    string ModelId,
    IReadOnlyList<EmbeddingVector> Vectors,
    ModelUsage Usage,
    ModelResponseMetadata Metadata);
