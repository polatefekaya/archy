using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.LanguageSemanticAdapters;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SemanticAnalysisRequest))]
[JsonSerializable(typeof(SemanticAnalysisResult))]
public sealed partial class SemanticAnalysisJsonContext : JsonSerializerContext;
