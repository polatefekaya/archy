using Archy.Features.Duplicates.PartitionEmbeddingCandidates;

namespace Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

/// <summary>Raw cosine plus its relative position in one language/complexity/size comparison population.</summary>
public sealed record RelativeEmbeddingSimilarity(
    string LeftMethodStableId,
    string RightMethodStableId,
    string Language,
    DuplicateComplexityBand ComplexityBand,
    DuplicateLineCountBand LineCountBand,
    double RawCosineSimilarity,
    double ZScore,
    int CandidatePopulationSize,
    int PairPopulationSize,
    double PopulationMean,
    double PopulationStandardDeviation);
