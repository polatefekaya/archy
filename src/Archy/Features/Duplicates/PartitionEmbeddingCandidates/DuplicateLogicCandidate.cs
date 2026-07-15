namespace Archy.Features.Duplicates.PartitionEmbeddingCandidates;

/// <summary>Normalized method metrics supplied by a language adapter before semantic duplicate comparison.</summary>
public sealed record DuplicateLogicCandidate(
    string MethodStableId,
    string Language,
    int LogicalLineCount,
    int CyclomaticComplexity,
    bool IsDataShape);
