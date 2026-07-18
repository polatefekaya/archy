using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Memory.ModelProviders.Deterministic;
using Archy.Features.Memory.ModelProviders.OpenAi;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.IndexEmbeddings;

/// <summary>Resolves only registered providers. The deterministic override is an explicit process-level test seam and never sends source remotely.</summary>
public sealed class EmbeddingModelProviderResolver(OpenAiResponsesModelProvider openAi)
    : IEmbeddingModelProviderResolver
{
    public Result<IModelProvider> Resolve(string configuredProvider)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("ARCHY_TEST_EMBEDDING_PROVIDER"), "deterministic", StringComparison.Ordinal))
        {
            return ResultFactory.Success<IModelProvider>(new DeterministicModelProvider(providerId: configuredProvider));
        }
        return configuredProvider.ToLowerInvariant() switch
        {
            "openai" => ResultFactory.Success<IModelProvider>(openAi),
            "disabled" => ResultFactory.Failure<IModelProvider>(Problem.Validation("No embedding provider is enabled.")),
            _ => ResultFactory.Failure<IModelProvider>(Problem.Validation("The configured embedding provider is unknown.")),
        };
    }
}
