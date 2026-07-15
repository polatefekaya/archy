namespace Archy.Features.Duplicates.SelectEmbeddingChunks;

public sealed record EmbeddingChunk(
    string MethodStableId,
    string RepositoryRelativePath,
    string Content,
    string ContentHash);
