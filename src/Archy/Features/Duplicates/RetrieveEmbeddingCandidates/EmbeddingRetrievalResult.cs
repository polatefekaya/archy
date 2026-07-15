namespace Archy.Features.Duplicates.RetrieveEmbeddingCandidates;

/// <summary>Revision-scoped bounded semantic-comparison input and its transparent fingerprint-work diagnostic.</summary>
public sealed record EmbeddingRetrievalResult(long GraphRevision, int CorpusSize, int FingerprintComparisons, IReadOnlyList<RetrievedEmbeddingCandidatePair> Pairs);
