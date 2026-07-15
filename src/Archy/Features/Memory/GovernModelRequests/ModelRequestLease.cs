namespace Archy.Features.Memory.GovernModelRequests;

public sealed record ModelRequestLease(
    string RunId,
    string RequestId,
    ModelRequestEstimate Estimate,
    DateTimeOffset GrantedAtUtc);
