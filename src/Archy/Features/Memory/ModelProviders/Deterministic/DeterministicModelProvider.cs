using System.Security.Cryptography;
using System.Text;
using Archy.Features.Memory.ModelProviders.Contracts;

namespace Archy.Features.Memory.ModelProviders.Deterministic;

/// <summary>A deterministic, offline provider for contract tests and local orchestration tests.</summary>
public sealed class DeterministicModelProvider(ModelProviderFailure? forcedFailure = null) : IModelProvider
{
    private static readonly ModelProviderDescriptor ProviderDescriptor = new(
        "deterministic",
        SupportsStructuredSummaries: true,
        SupportsEmbeddings: true);

    public ModelProviderDescriptor Descriptor => ProviderDescriptor;

    public ValueTask<ModelProviderResult<StructuredSummaryResponse>> GenerateSummaryAsync(
        SummaryGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(ModelProviderResults.Fail<StructuredSummaryResponse>(ModelProviderRequestValidator.Cancelled()));
        }

        var invalid = ModelProviderRequestValidator.Validate(request);
        if (invalid is not null)
        {
            return ValueTask.FromResult(ModelProviderResults.Fail<StructuredSummaryResponse>(invalid));
        }

        if (forcedFailure is not null)
        {
            return ValueTask.FromResult(ModelProviderResults.Fail<StructuredSummaryResponse>(forcedFailure));
        }

        var hash = Hash($"summary|{request.ModelId}|{request.Prompt}|{request.OutputSchemaJson}");
        var response = new StructuredSummaryResponse(
            request.RequestId,
            request.ModelId,
            $"{{\"summary\":\"deterministic:{hash[..16]}\"}}",
            Usage(request.Prompt.Length, outputTokens: 8),
            new ModelResponseMetadata($"deterministic:{hash[..24]}", "{\"deterministic\":true}"));
        return ValueTask.FromResult(ModelProviderResults.Success(response));
    }

    public ValueTask<ModelProviderResult<EmbeddingGenerationResponse>> GenerateEmbeddingsAsync(
        EmbeddingGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(ModelProviderResults.Fail<EmbeddingGenerationResponse>(ModelProviderRequestValidator.Cancelled()));
        }

        var invalid = ModelProviderRequestValidator.Validate(request);
        if (invalid is not null)
        {
            return ValueTask.FromResult(ModelProviderResults.Fail<EmbeddingGenerationResponse>(invalid));
        }

        if (forcedFailure is not null)
        {
            return ValueTask.FromResult(ModelProviderResults.Fail<EmbeddingGenerationResponse>(forcedFailure));
        }

        var vectors = request.Inputs
            .Select(static input => new EmbeddingVector(input.InputId, ToVector(input.Content)))
            .ToArray();
        var requestHash = Hash(string.Join("\n", request.Inputs.Select(static input => $"{input.InputId}\u001f{input.Content}")));
        var response = new EmbeddingGenerationResponse(
            request.RequestId,
            request.ModelId,
            vectors,
            Usage(request.Inputs.Sum(static input => input.Content.Length), outputTokens: 0),
            new ModelResponseMetadata($"deterministic:{requestHash[..24]}", "{\"deterministic\":true}"));
        return ValueTask.FromResult(ModelProviderResults.Success(response));
    }

    private static float[] ToVector(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        var vector = new float[8];
        var magnitude = 0d;
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (bytes[index] - 127.5f) / 127.5f;
            magnitude += vector[index] * vector[index];
        }

        var divisor = (float)Math.Sqrt(magnitude);
        return divisor == 0
            ? vector
            : [.. vector.Select(value => value / divisor)];
    }

    private static ModelUsage Usage(int inputCharacters, int outputTokens) => new(
        InputTokens: Math.Max(1, (inputCharacters + 3) / 4),
        OutputTokens: outputTokens,
        CachedInputTokens: 0,
        TotalTokens: Math.Max(1, (inputCharacters + 3) / 4) + outputTokens);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
