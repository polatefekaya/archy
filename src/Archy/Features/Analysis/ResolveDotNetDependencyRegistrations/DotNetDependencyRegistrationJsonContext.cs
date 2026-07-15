using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DotNetDependencyRegistrationEvidence))]
internal sealed partial class DotNetDependencyRegistrationJsonContext : JsonSerializerContext;

internal sealed record DotNetDependencyRegistrationEvidence(
    string Method,
    string ServiceType,
    string ImplementationType,
    string RepositoryRelativePath,
    int Line,
    int Column);
