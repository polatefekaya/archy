using Archy.Features.Analysis.ProviderJoinKeys;
using Archy.Features.Analysis.ProviderSiteMatches;

namespace Archy.Features.Analysis.ProviderEdgeEmissions;

public sealed record ProviderEdgeEmissionIntent(
    string EdgeKind,
    string ProviderId,
    string SourceStableId,
    string TargetStableId,
    ProviderConfidenceTier ConfidenceTier,
    string Rationale,
    ProviderSiteMatch Site,
    ProviderJoinKey JoinKey);

public enum ProviderConfidenceTier
{
    Semantic,
    ExplicitSyntax,
    LiteralPattern,
    Advisory,
}
