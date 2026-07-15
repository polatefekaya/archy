using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ExtractCSharpSyntaxFacts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CSharpSyntaxEvidence))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class CSharpSyntaxFactJsonContext : JsonSerializerContext;

internal sealed record CSharpSyntaxEvidence(
    string Kind,
    string RepositoryRelativePath,
    int? StartLine,
    int? EndLine,
    string? Detail);
