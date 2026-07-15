namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record EmbeddingGenerationRequest(
    string RequestId,
    string ModelId,
    IReadOnlyList<EmbeddingInput> Inputs);
