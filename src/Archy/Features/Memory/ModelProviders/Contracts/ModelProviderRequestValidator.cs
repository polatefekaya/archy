using System.Text.Json;

namespace Archy.Features.Memory.ModelProviders.Contracts;

internal static class ModelProviderRequestValidator
{
    internal static ModelProviderFailure? Validate(SummaryGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestId) ||
            string.IsNullOrWhiteSpace(request.SummaryBatchId) ||
            string.IsNullOrWhiteSpace(request.ModelId) ||
            string.IsNullOrWhiteSpace(request.Prompt) ||
            request.MaxOutputTokens is < 1 or > 100_000 ||
            !IsJson(request.OutputSchemaJson))
        {
            return InvalidRequest("Structured summary requests require IDs, a model, a bounded prompt, a positive output budget, and a JSON output schema.");
        }

        return null;
    }

    internal static ModelProviderFailure? Validate(EmbeddingGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestId) ||
            string.IsNullOrWhiteSpace(request.ModelId) ||
            request.Inputs is null ||
            request.Inputs.Count == 0 ||
            request.Inputs.Count > 2_048 ||
            request.Inputs.Any(static input => string.IsNullOrWhiteSpace(input.InputId) || string.IsNullOrWhiteSpace(input.Content)) ||
            request.Inputs.Select(static input => input.InputId).Distinct(StringComparer.Ordinal).Count() != request.Inputs.Count)
        {
            return InvalidRequest("Embedding requests require a model and up to 2,048 distinct non-empty inputs.");
        }

        return null;
    }

    internal static ModelProviderFailure Cancelled() => new(
        ModelProviderFailureKind.Cancelled,
        "The model request was cancelled.",
        IsRetryable: true,
        RetryAfter: null);

    private static ModelProviderFailure InvalidRequest(string message) => new(
        ModelProviderFailureKind.InvalidRequest,
        message,
        IsRetryable: false,
        RetryAfter: null);

    private static bool IsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
