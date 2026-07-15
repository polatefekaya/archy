using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Memory.GovernModelRequests;

namespace Archy.UnitTests.Features.Memory.GovernModelRequests;

public sealed class ModelRequestGovernorTests
{
    [Fact]
    public void TryReserveEnforcesTokenBudgetAndReturnsAnIdempotentExistingLease()
    {
        var configuration = new ModelConfiguration("openai", null, null, 2, 100, 1, 1, 0);
        var governor = new ModelRequestGovernor(TimeProvider.System);

        var first = governor.TryReserve("run", "request", new ModelRequestEstimate(75, .5), configuration);
        var repeated = governor.TryReserve("run", "request", new ModelRequestEstimate(75, .5), configuration);
        governor.Complete(first.Lease!);
        var overBudget = governor.TryReserve("run", "next", new ModelRequestEstimate(30, .1), configuration);

        Assert.True(first.IsGranted);
        Assert.Same(first.Lease, repeated.Lease);
        Assert.Equal(ModelGovernorDenial.TokenQuotaExhausted, overBudget.Denial);
    }

    [Fact]
    public void ApplyRateLimitCooldownBlocksNewRequests()
    {
        var configuration = new ModelConfiguration("openai", null, null, 2, 100, 1, 1, 2);
        var governor = new ModelRequestGovernor(TimeProvider.System);
        governor.ApplyRateLimitCooldown(TimeSpan.FromSeconds(5), configuration);

        var decision = governor.TryReserve("run", "request", new ModelRequestEstimate(1, 0), configuration);

        Assert.Equal(ModelGovernorDenial.CooldownActive, decision.Denial);
        Assert.True(decision.RetryAfter > TimeSpan.Zero);
    }
}
