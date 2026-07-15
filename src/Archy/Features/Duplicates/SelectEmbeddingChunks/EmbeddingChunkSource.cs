namespace Archy.Features.Duplicates.SelectEmbeddingChunks;

/// <summary>An immutable, hash-verified source snapshot range supplied by the analysis pipeline.</summary>
public sealed record EmbeddingChunkSource(
    string MethodStableId,
    string RepositoryRelativePath,
    int StartLine,
    int EndLine,
    string SourceText);
