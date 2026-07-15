using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ResolveDotNetMessageContracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DotNetMessageContractEvidence))]
internal sealed partial class DotNetMessageContractJsonContext : JsonSerializerContext;

internal sealed record DotNetMessageContractEvidence(
    string Method,
    string MessageType,
    string ProducerStableId,
    string ConsumerStableId,
    string RepositoryRelativePath,
    int Line,
    int Column);
