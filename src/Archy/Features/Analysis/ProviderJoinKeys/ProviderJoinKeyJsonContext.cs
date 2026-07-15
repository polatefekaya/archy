using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ProviderJoinKeys;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProviderJoinKey))]
[JsonSerializable(typeof(ProviderJoinKey[]))]
public sealed partial class ProviderJoinKeyJsonContext : JsonSerializerContext;
