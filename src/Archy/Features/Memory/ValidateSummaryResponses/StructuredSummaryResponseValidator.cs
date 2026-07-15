using System.Text.Json;

namespace Archy.Features.Memory.ValidateSummaryResponses;

/// <summary>Rejects untrusted model output before it can become immutable architecture memory.</summary>
public sealed class StructuredSummaryResponseValidator : IStructuredSummaryResponseValidator
{
    private static readonly HashSet<string> RequiredProperties = new(StringComparer.Ordinal)
    {
        "targetStableId",
        "summary",
        "englishDiff",
    };

    public SummaryResponseValidation Validate(SummaryResponseValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Batch);
        ArgumentNullException.ThrowIfNull(request.Response);
        if (!string.Equals(request.ExpectedRequestId, request.Response.RequestId, StringComparison.Ordinal) ||
            !string.Equals(request.ExpectedModelId, request.Response.ModelId, StringComparison.Ordinal))
        {
            return Degraded("The model response does not match the issued request or configured model.");
        }

        if (request.Response.Usage.InputTokens < 0 || request.Response.Usage.OutputTokens < 0 ||
            request.Response.Usage.CachedInputTokens < 0 || request.Response.Usage.TotalTokens < 0 ||
            string.IsNullOrWhiteSpace(request.Response.Metadata.ProviderRequestId) || !IsJson(request.Response.Metadata.MetadataJson))
        {
            return Degraded("The model response metadata or usage counters are invalid.");
        }

        if (!request.Batch.Members.Any(member => string.Equals(member.TargetStableId, request.ExpectedTargetStableId, StringComparison.Ordinal)))
        {
            return Degraded("The requested summary target is not a member of the immutable summary batch.");
        }

        try
        {
            using var document = JsonDocument.Parse(request.Response.OutputJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Retryable("The model response must be one JSON object, not prose or an array.");
            }

            var names = root.EnumerateObject().Select(static property => property.Name).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                !names.All(RequiredProperties.Contains) ||
                !RequiredProperties.SetEquals(names))
            {
                return Retryable("The model response must contain exactly targetStableId, summary, and englishDiff.");
            }

            var targetId = RequiredString(root, "targetStableId");
            var summary = RequiredString(root, "summary");
            var englishDiff = RequiredString(root, "englishDiff");
            if (targetId is null || summary is null || englishDiff is null ||
                !string.Equals(targetId, request.ExpectedTargetStableId, StringComparison.Ordinal))
            {
                return Retryable("The model response contains an unknown or mismatched target ID.");
            }

            if (!IsQualityText(summary, 20, 6_000) || !IsQualityText(englishDiff, 1, 3_000))
            {
                return Retryable("The model response contains empty, oversized, control-character, refusal, or non-substantive summary text.");
            }

            return new SummaryResponseValidation(
                SummaryResponseValidationDisposition.Accepted,
                new ValidatedSummaryContent(targetId, summary, englishDiff),
                null);
        }
        catch (JsonException)
        {
            return Retryable("The model response is not valid JSON.");
        }
    }

    private static SummaryResponseValidation Retryable(string message) =>
        new(SummaryResponseValidationDisposition.RetryableFailure, null, message);

    private static SummaryResponseValidation Degraded(string message) =>
        new(SummaryResponseValidationDisposition.DegradedFailure, null, message);

    private static string? RequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool IsQualityText(string value, int minimumLength, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 1 or > 6_000 || value.Length < minimumLength || value.Length > maximumLength ||
            value.Any(static character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("As an AI", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3 || minimumLength <= 1;
    }

    private static bool IsJson(string value)
    {
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
