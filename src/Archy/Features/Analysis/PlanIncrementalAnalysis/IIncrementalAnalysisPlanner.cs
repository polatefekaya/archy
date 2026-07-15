using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Analysis.PlanIncrementalAnalysis;

public interface IIncrementalAnalysisPlanner
{
    IncrementalAnalysisPlan Create(SourceInventory inventory, GraphRevisionSnapshot? activeRevision);
}
