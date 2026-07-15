using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archy.Features.Analysis.ProviderJoinKeys;
using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ProviderEdgeEmissions;

public sealed class ProviderEdgeEmitter : IProviderEdgeEmitter
{
    public Result<GraphEdgeFact> Emit(ProviderEdgeEmissionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var problem = Validate(intent);
        if (problem is not null)
        {
            return ResultFactory.Failure<GraphEdgeFact>(problem);
        }

        var normalizedJoinKey = string.Concat(
            ToWireValue(intent.JoinKey.Strategy),
            ":",
            intent.JoinKey.NormalizedValue);
        var edgeId = EdgeId(
            intent.SourceStableId,
            intent.TargetStableId,
            intent.EdgeKind,
            normalizedJoinKey);
        return ResultFactory.Success(new GraphEdgeFact(
            edgeId,
            intent.SourceStableId,
            intent.TargetStableId,
            intent.EdgeKind,
            normalizedJoinKey,
            intent.ProviderId,
            Confidence(intent.ConfidenceTier),
            JsonSerializer.Serialize(
                new ProviderEdgeEvidence(
                    intent.Rationale,
                    intent.ConfidenceTier,
                    intent.Site.Evidence,
                    intent.JoinKey),
                ProviderEdgeEmissionJsonContext.Default.ProviderEdgeEvidence)));
    }

    public static Problem? Validate(ProviderEdgeEmissionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.EdgeKind) ||
            string.IsNullOrWhiteSpace(intent.ProviderId) ||
            string.IsNullOrWhiteSpace(intent.SourceStableId) ||
            string.IsNullOrWhiteSpace(intent.TargetStableId) ||
            string.Equals(intent.SourceStableId, intent.TargetStableId, StringComparison.Ordinal) ||
            !Enum.IsDefined(intent.ConfidenceTier) ||
            string.IsNullOrWhiteSpace(intent.Rationale))
        {
            return Problem.Validation("Provider edge emissions require distinct source and target nodes, an edge kind, provider, and rationale.");
        }

        var siteProblem = ProviderSiteMatchContract.Validate(intent.Site);
        if (siteProblem is not null)
        {
            return siteProblem;
        }

        if (intent.Site.State != ProviderSiteMatchState.Matched)
        {
            return Problem.Validation("Only matched provider sites can emit graph edges.");
        }

        return ProviderJoinKeyContract.Validate(intent.JoinKey);
    }

    private static double Confidence(ProviderConfidenceTier tier) => tier switch
    {
        ProviderConfidenceTier.Semantic => 1,
        ProviderConfidenceTier.ExplicitSyntax => 0.8,
        ProviderConfidenceTier.LiteralPattern => 0.65,
        ProviderConfidenceTier.Advisory => 0.4,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown provider confidence tier."),
    };

    private static string ToWireValue(ProviderJoinStrategy strategy) => strategy switch
    {
        ProviderJoinStrategy.TypeEquality => "type_equality",
        ProviderJoinStrategy.StringEquality => "string_equality",
        ProviderJoinStrategy.CanonicalPattern => "canonical_pattern",
        ProviderJoinStrategy.ConfigurationPath => "configuration_path",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown provider join strategy."),
    };

    private static string EdgeId(string sourceStableId, string targetStableId, string edgeKind, string joinKey) =>
        $"edge:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceStableId}\u001f{targetStableId}\u001f{edgeKind}\u001f{joinKey}")))}";
}
