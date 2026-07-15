namespace Archy.Features.Memory.GovernModelRequests;

public sealed record ModelGovernorDecision(
    ModelRequestLease? Lease,
    ModelGovernorDenial Denial,
    TimeSpan? RetryAfter)
{
    public bool IsGranted => Lease is not null && Denial == ModelGovernorDenial.None;
}
