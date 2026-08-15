using Archy.Features.Similarity.RetrieveHybridCandidates;

namespace Archy.Features.Queries.FindSimilar;

public sealed record SimilarCodeQuery(string Query, string? SourceStableId, int Limit, HybridSimilarityPolicy? Policy = null, IReadOnlyDictionary<string, string>? LayerNamesByStableId = null);
