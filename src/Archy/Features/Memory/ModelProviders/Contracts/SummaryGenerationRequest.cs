namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record SummaryGenerationRequest(
    string RequestId,
    string SummaryBatchId,
    string ModelId,
    string Prompt,
    string OutputSchemaJson,
    int MaxOutputTokens);
