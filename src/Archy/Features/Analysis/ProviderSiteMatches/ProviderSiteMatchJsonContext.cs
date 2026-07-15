using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ProviderSiteMatches;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProviderSiteMatch))]
[JsonSerializable(typeof(ProviderSiteMatch[]))]
public sealed partial class ProviderSiteMatchJsonContext : JsonSerializerContext;
