using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Architecture.ArchitectureExceptions;

public sealed class ArchitectureExceptionApplicatorTests
{
    private readonly ArchitectureExceptionApplicator applicator = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-07-15T12:00:00+00:00",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void SuppressesOnlyTheExactIntroducedFindingWhileTheDecisionIsActive()
    {
        var comparison = Comparison("finding:one", ArchitectureFindingStatus.Introduced);
        var policy = new ArchitectureExceptionPolicy(
            1,
            [Decision("exception:one", "finding:one", Now.AddDays(1), Now.AddDays(2))]);

        var result = applicator.Apply(comparison, policy, Now);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.False(result.Value.Comparison.HasIntroducedFindings);
        var outcome = Assert.Single(result.Value.Comparison.Findings);
        Assert.Equal(ArchitectureFindingStatus.Excepted, outcome.Status);
        var status = Assert.Single(result.Value.Exceptions);
        Assert.Equal(ArchitectureExceptionState.Active, status.State);
        Assert.True(status.AppliesToIntroducedFinding);
    }

    [Fact]
    public void KeepsTheExactFindingBlockingWhenItsDecisionExpired()
    {
        var comparison = Comparison("finding:one", ArchitectureFindingStatus.Introduced);
        var policy = new ArchitectureExceptionPolicy(
            1,
            [Decision("exception:one", "finding:one", Now.AddDays(-2), Now.AddMinutes(-1))]);

        var result = applicator.Apply(comparison, policy, Now);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.True(result.Value.Comparison.HasIntroducedFindings);
        var outcome = Assert.Single(result.Value.Comparison.Findings);
        Assert.Equal(ArchitectureFindingStatus.Introduced, outcome.Status);
        var status = Assert.Single(result.Value.Exceptions);
        Assert.Equal(ArchitectureExceptionState.Expired, status.State);
        Assert.False(status.AppliesToIntroducedFinding);
    }

    [Fact]
    public void KeepsAnUnexpiredExceptionEffectiveButMakesAnOverdueReviewVisible()
    {
        var comparison = Comparison("finding:one", ArchitectureFindingStatus.Introduced);
        var policy = new ArchitectureExceptionPolicy(
            1,
            [Decision("exception:one", "finding:one", Now.AddDays(-1), Now.AddDays(2))]);

        var result = applicator.Apply(comparison, policy, Now);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Comparison.HasIntroducedFindings);
        var status = Assert.Single(result.Value.Exceptions);
        Assert.Equal(ArchitectureExceptionState.ReviewDue, status.State);
        Assert.True(status.AppliesToIntroducedFinding);
    }

    [Fact]
    public void DoesNotSuppressLegacyFindingsOrUnrelatedFindingKeys()
    {
        var comparison = new ArchitectureBaselineComparison(
            ArchitectureBaselineStatus.Compatible,
            "/repo/archy.baseline.json",
            1,
            [
                new ArchitectureFindingOutcome(Finding("finding:legacy"), ArchitectureFindingStatus.Legacy),
                new ArchitectureFindingOutcome(Finding("finding:introduced"), ArchitectureFindingStatus.Introduced),
            ]);
        var policy = new ArchitectureExceptionPolicy(
            1,
            [Decision("exception:legacy", "finding:legacy", Now.AddDays(1), Now.AddDays(2))]);

        var result = applicator.Apply(comparison, policy, Now);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Comparison.HasIntroducedFindings);
        Assert.Collection(
            result.Value.Comparison.Findings,
            outcome => Assert.Equal(ArchitectureFindingStatus.Legacy, outcome.Status),
            outcome => Assert.Equal(ArchitectureFindingStatus.Introduced, outcome.Status));
        var status = Assert.Single(result.Value.Exceptions);
        Assert.Equal(ArchitectureExceptionState.Unused, status.State);
    }

    private static ArchitectureBaselineComparison Comparison(string key, ArchitectureFindingStatus status) => new(
        ArchitectureBaselineStatus.Missing,
        "/repo/archy.baseline.json",
        null,
        [new ArchitectureFindingOutcome(Finding(key), status)]);

    private static ArchitectureFinding Finding(string key) => new(
        key,
        ArchitectureFindingKind.LayerDependency,
        key,
        [new ArchitectureTarget(ArchitectureTargetKind.Rule, "fixture-rule")]);

    private static ArchitectureExceptionDecision Decision(
        string exceptionId,
        string findingKey,
        DateTimeOffset review,
        DateTimeOffset expiry) => new(
        exceptionId,
        findingKey,
        "fixture-author",
        "The fixture documents a narrowly reviewed exception.",
        review,
        expiry,
        Now.AddDays(-3));
}
