namespace Archy.Features.Memory.ModelProviders.Contracts;

public enum ModelProviderFailureKind
{
    InvalidRequest,
    Authentication,
    Authorization,
    RateLimited,
    Transient,
    Timeout,
    Unavailable,
    Cancelled,
    InvalidResponse,
}
