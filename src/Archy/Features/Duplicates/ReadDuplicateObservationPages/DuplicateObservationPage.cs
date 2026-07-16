using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ReadDuplicateObservationPages;

public sealed record DuplicateObservationPage(string FindingId, ArchitectureTarget LeftTarget, ArchitectureTarget RightTarget, int Offset, int Limit, int TotalCount, IReadOnlyList<DuplicateObservationPageItem> Items);
