namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record EmbeddingVector(string InputId, IReadOnlyList<float> Values);
