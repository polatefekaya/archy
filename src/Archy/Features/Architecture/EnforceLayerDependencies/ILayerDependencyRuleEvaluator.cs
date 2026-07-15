using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.EnforceLayerDependencies;

public interface ILayerDependencyRuleEvaluator
{
    Result<LayerDependencyEvaluation> Evaluate(
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        IReadOnlyList<LayerRuleConfiguration> layers,
        ArchitectureEnforcementConfiguration enforcement);
}
