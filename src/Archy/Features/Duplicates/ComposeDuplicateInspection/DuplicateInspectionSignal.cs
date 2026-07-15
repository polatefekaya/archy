using Archy.Features.Duplicates.DuplicateFindings;

namespace Archy.Features.Duplicates.ComposeDuplicateInspection;

public sealed record DuplicateInspectionSignal(DuplicateSignalKind Kind, double Score, string EvidenceJson);
