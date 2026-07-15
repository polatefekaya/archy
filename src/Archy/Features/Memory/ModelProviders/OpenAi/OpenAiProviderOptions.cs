namespace Archy.Features.Memory.ModelProviders.OpenAi;

public sealed record OpenAiProviderOptions(
    Uri BaseAddress,
    TimeSpan RequestTimeout,
    int MaxAttempts)
{
    public static OpenAiProviderOptions Default { get; } = new(
        new Uri("https://api.openai.com/v1/", UriKind.Absolute),
        TimeSpan.FromSeconds(45),
        MaxAttempts: 3);
}
