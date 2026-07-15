namespace Archy.Features.Memory.ModelProviders.Contracts;

public sealed record ModelProviderFailure(
    ModelProviderFailureKind Kind,
    string Message,
    bool IsRetryable,
    TimeSpan? RetryAfter);
