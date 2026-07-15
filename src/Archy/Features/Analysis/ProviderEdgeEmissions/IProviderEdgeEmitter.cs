using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ProviderEdgeEmissions;

public interface IProviderEdgeEmitter
{
    Result<GraphEdgeFact> Emit(ProviderEdgeEmissionIntent intent);
}
