namespace Archy.Features.Duplicates.RetrieveEmbeddingCandidates;

public sealed record RetrievedEmbeddingCandidatePair(string LeftMethodStableId, string RightMethodStableId, int FingerprintHammingDistance);
