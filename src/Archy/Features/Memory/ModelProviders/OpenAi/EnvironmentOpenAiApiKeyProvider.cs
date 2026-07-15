namespace Archy.Features.Memory.ModelProviders.OpenAi;

/// <summary>Default local development credential source; repository configuration is deliberately not a credential source.</summary>
public sealed class EnvironmentOpenAiApiKeyProvider : IOpenAiApiKeyProvider
{
    public string? GetApiKey() => Environment.GetEnvironmentVariable("OPENAI_API_KEY");
}
