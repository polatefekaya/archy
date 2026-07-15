using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.MapSemanticGraphFacts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SemanticGraphFactMapper.SemanticEvidence))]
internal sealed partial class SemanticGraphFactJsonContext : JsonSerializerContext;
