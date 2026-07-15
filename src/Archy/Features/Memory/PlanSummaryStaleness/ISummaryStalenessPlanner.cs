using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Memory.ComparePublicSurface;

namespace Archy.Features.Memory.PlanSummaryStaleness;

public interface ISummaryStalenessPlanner
{
    SummaryStalenessPlan Plan(
        GraphRevisionSnapshot prior,
        GraphRevisionSnapshot current,
        PublicSurfaceDiff publicSurfaceDiff);
}
