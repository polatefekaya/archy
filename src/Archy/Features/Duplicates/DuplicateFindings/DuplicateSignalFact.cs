namespace Archy.Features.Duplicates.DuplicateFindings;

public sealed record DuplicateSignalFact(
    DuplicateSignalKind Kind,
    double Score,
    string EvidenceJson);
