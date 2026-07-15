using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Memory.GovernModelRequests;

public interface IModelRequestGovernor
{
    ModelGovernorDecision TryReserve(string runId, string requestId, ModelRequestEstimate estimate, ModelConfiguration configuration);

    void Complete(ModelRequestLease lease);

    void ApplyRateLimitCooldown(TimeSpan? retryAfter, ModelConfiguration configuration);
}
