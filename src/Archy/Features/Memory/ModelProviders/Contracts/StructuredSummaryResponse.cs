namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record StructuredSummaryResponse(
    string RequestId,
    string ModelId,
    string OutputJson,
    ModelUsage Usage,
    ModelResponseMetadata Metadata);
