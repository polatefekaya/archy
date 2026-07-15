using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Memory.GovernModelRequests;

/// <summary>In-memory hard admission control. Durable retry state belongs to the failure-queue slice, not this hot-path governor.</summary>
public sealed class ModelRequestGovernor(TimeProvider timeProvider) : IModelRequestGovernor
{
    private readonly object gate = new();
    private readonly Dictionary<string, RunUsage> usageByRun = new(StringComparer.Ordinal);
    private readonly Dictionary<(string RunId, string RequestId), ModelRequestLease> active = new();
    private DateTimeOffset? cooldownUntilUtc;

    public ModelGovernorDecision TryReserve(string runId, string requestId, ModelRequestEstimate estimate, ModelConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(configuration);
        if (estimate.ReservedTokens is < 1 or > 10_000_000 || double.IsNaN(estimate.ReservedCostUsd) || double.IsInfinity(estimate.ReservedCostUsd) || estimate.ReservedCostUsd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimate));
        }

        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            if (cooldownUntilUtc is { } cooldown && cooldown > now)
            {
                return Denied(ModelGovernorDenial.CooldownActive, cooldown - now);
            }

            var key = (runId, requestId);
            if (active.TryGetValue(key, out var existing))
            {
                return new ModelGovernorDecision(existing, ModelGovernorDenial.None, null);
            }

            if (active.Count >= configuration.MaxConcurrentRequests)
            {
                return Denied(ModelGovernorDenial.ConcurrencyLimitReached, null);
            }

            if (!usageByRun.TryGetValue(runId, out var usage))
            {
                usage = new RunUsage();
                usageByRun.Add(runId, usage);
            }

            if (usage.ReservedRequestCount >= configuration.MaxRequestsPerRun)
            {
                return Denied(ModelGovernorDenial.RequestQuotaExhausted, null);
            }

            if (usage.ReservedTokens > configuration.MaxTokensPerRun - estimate.ReservedTokens)
            {
                return Denied(ModelGovernorDenial.TokenQuotaExhausted, null);
            }

            if (usage.ReservedCostUsd > configuration.MaxCostUsdPerRun - estimate.ReservedCostUsd)
            {
                return Denied(ModelGovernorDenial.CostQuotaExhausted, null);
            }

            var lease = new ModelRequestLease(runId, requestId, estimate, now);
            active.Add(key, lease);
            usage.ReservedRequestCount++;
            usage.ReservedTokens += estimate.ReservedTokens;
            usage.ReservedCostUsd += estimate.ReservedCostUsd;
            return new ModelGovernorDecision(lease, ModelGovernorDenial.None, null);
        }
    }

    public void Complete(ModelRequestLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (gate)
        {
            active.Remove((lease.RunId, lease.RequestId));
        }
    }

    public void ApplyRateLimitCooldown(TimeSpan? retryAfter, ModelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var minimumCooldown = TimeSpan.FromSeconds(configuration.RateLimitCooldownSeconds);
        var requestedCooldown = retryAfter.GetValueOrDefault();
        var duration = requestedCooldown > minimumCooldown ? requestedCooldown : minimumCooldown;
        lock (gate)
        {
            var requestedUntil = timeProvider.GetUtcNow() + duration;
            if (cooldownUntilUtc is null || requestedUntil > cooldownUntilUtc)
            {
                cooldownUntilUtc = requestedUntil;
            }
        }
    }

    private static ModelGovernorDecision Denied(ModelGovernorDenial denial, TimeSpan? retryAfter) => new(null, denial, retryAfter);

    private sealed class RunUsage
    {
        public int ReservedRequestCount { get; set; }
        public int ReservedTokens { get; set; }
        public double ReservedCostUsd { get; set; }
    }
}
