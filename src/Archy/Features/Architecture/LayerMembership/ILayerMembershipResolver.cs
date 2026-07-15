using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.LayerMembership;

public interface ILayerMembershipResolver
{
    Result<IReadOnlyList<LayerMembershipResolution>> Resolve(
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<LayerRuleConfiguration> layers);
}
