using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LanguageServerLaunchSpecification))]
[JsonSerializable(typeof(LspInitializeRequest))]
[JsonSerializable(typeof(LanguageServerCapabilityProfile))]
public sealed partial class LanguageServerProtocolJsonContext : JsonSerializerContext;
