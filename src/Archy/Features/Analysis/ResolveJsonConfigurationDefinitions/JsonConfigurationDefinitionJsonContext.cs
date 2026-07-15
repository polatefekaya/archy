using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JsonConfigurationFileEvidence))]
[JsonSerializable(typeof(JsonConfigurationDefinitionEvidence))]
[JsonSerializable(typeof(JsonConfigurationKeyEvidence))]
internal sealed partial class JsonConfigurationDefinitionJsonContext : JsonSerializerContext;

internal sealed record JsonConfigurationFileEvidence(
    string RepositoryRelativePath,
    string ContentHash);

internal sealed record JsonConfigurationDefinitionEvidence(
    string KeyPath,
    string JsonPointer,
    string RepositoryRelativePath,
    int Line,
    int Column);

internal sealed record JsonConfigurationKeyEvidence(
    string KeyPath,
    IReadOnlyList<JsonConfigurationDefinitionEvidence> Definitions);
