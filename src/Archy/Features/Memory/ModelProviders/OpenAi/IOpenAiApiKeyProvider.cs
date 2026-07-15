namespace Archy.Features.Memory.ModelProviders.OpenAi;

/// <summary>Supplies a user credential at execution time. Implementations must never persist or log the returned value.</summary>
public interface IOpenAiApiKeyProvider
{
    string? GetApiKey();
}
