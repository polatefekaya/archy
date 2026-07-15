namespace Archy.Features.Duplicates.RetrieveEmbeddingCandidates;

/// <summary>Hard bound on exact vector comparisons made for any method within one comparison band.</summary>
public sealed record EmbeddingRetrievalOptions(int MaxCandidatesPerMethod)
{
    public static EmbeddingRetrievalOptions Default { get; } = new(32);
}
