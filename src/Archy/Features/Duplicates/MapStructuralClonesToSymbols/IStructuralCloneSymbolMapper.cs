using Archy.Features.Duplicates.ParseJscpdCloneReport;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Duplicates.MapStructuralClonesToSymbols;

public interface IStructuralCloneSymbolMapper
{
    StructuralCloneSymbolMapping Map(GraphRevisionSnapshot graph, IReadOnlyList<StructuralCloneOccurrence> occurrences);
}
