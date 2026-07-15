using System.Text.Json.Serialization;

namespace Archy.Features.Analysis.ResolveRabbitMqTopology;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RabbitMqResourceEvidence))]
[JsonSerializable(typeof(RabbitMqOperationEvidence))]
internal sealed partial class RabbitMqTopologyJsonContext : JsonSerializerContext;

internal sealed record RabbitMqResourceEvidence(
    string ResourceKind,
    string ResourceName,
    string RepositoryRelativePath,
    int Line,
    int Column);

internal sealed record RabbitMqOperationEvidence(
    string Operation,
    string? Exchange,
    string? Queue,
    string? RoutingKey,
    string? SourceStableId,
    string? ConsumerStableId,
    string RepositoryRelativePath,
    int Line,
    int Column);
