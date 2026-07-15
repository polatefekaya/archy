using System.Text.Json.Serialization;
using Archy.Features.Analysis.ProviderJoinKeys;
using Archy.Features.Analysis.ProviderSiteMatches;

namespace Archy.Features.Analysis.ProviderEdgeEmissions;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProviderEdgeEvidence))]
internal sealed partial class ProviderEdgeEmissionJsonContext : JsonSerializerContext;

internal sealed record ProviderEdgeEvidence(
    string Rationale,
    ProviderConfidenceTier ConfidenceTier,
    ProviderSiteEvidence Site,
    ProviderJoinKey JoinKey);
