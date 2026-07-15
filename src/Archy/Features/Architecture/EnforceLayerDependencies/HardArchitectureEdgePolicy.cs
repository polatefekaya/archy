using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Architecture.EnforceLayerDependencies;

/// <summary>Allows only configured, fully certain graph edges to enforce architecture rules.</summary>
public sealed class HardArchitectureEdgePolicy : IHardArchitectureEdgePolicy
{
    public bool IsEligible(GraphEdgeFact edge, ArchitectureEnforcementConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(configuration);
        return edge.Confidence == 1 && configuration.HardEdgeKinds.Contains(edge.EdgeKind, StringComparer.Ordinal);
    }
}
