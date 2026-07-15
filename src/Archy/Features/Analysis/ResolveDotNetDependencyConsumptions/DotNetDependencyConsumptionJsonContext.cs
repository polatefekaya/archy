using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DotNetDependencyConsumptionEvidence))]
internal sealed partial class DotNetDependencyConsumptionJsonContext : JsonSerializerContext;

internal sealed record DotNetDependencyConsumptionEvidence(
    string ConsumerType,
    string ServiceType,
    string ParameterName,
    string RepositoryRelativePath,
    int Line,
    int Column,
    int ExplicitRegistrationCount);
