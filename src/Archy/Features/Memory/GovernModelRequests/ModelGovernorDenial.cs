namespace Archy.Features.Memory.GovernModelRequests;

public enum ModelGovernorDenial
{
    None,
    RequestQuotaExhausted,
    TokenQuotaExhausted,
    CostQuotaExhausted,
    ConcurrencyLimitReached,
    CooldownActive,
}
