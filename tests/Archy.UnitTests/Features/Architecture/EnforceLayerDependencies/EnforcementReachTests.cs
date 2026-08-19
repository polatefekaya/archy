using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.VerificationBaselines;

namespace Archy.UnitTests.Features.Architecture.EnforceLayerDependencies;

/// <summary>
/// A gate that cannot fail must not look like a gate that found nothing wrong.
/// </summary>
public sealed class EnforcementReachTests
{
    private readonly ArchitectureFindingFactory factory = new();

    [Fact]
    public void TwoPopulatedLayersWithNoEligibleEdgeIsUnenforceable()
    {
        var reach = new EnforcementReach(0, 12, 2, ["references"], ["declares", "using"]);

        Assert.True(reach.IsUnenforceable);
    }

    [Fact]
    public void ASinglePopulatedLayerHasNothingToEnforceAndIsNotReported()
    {
        // No direction rule can apply between a layer and itself, so silence here is honest.
        var reach = new EnforcementReach(0, 12, 1, ["references"], ["declares", "using"]);

        Assert.False(reach.IsUnenforceable);
    }

    [Fact]
    public void ARepositoryWithNoDependenciesAtAllIsNotReported()
    {
        var reach = new EnforcementReach(0, 0, 1, ["references"], []);

        Assert.False(reach.IsUnenforceable);
    }

    [Fact]
    public void OneEligibleEdgeIsEnoughForTheGateToBeReal()
    {
        var reach = new EnforcementReach(1, 12, 4, ["references"], ["references"]);

        Assert.False(reach.IsUnenforceable);
    }

    [Fact]
    public void AnUnenforceableEvaluationProducesABlockingFinding()
    {
        var result = factory.Create(Evaluation(new EnforcementReach(0, 4, 2, ["calls", "references"], ["declares", "using"])));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var finding = Assert.Single(result.Value);
        Assert.Equal(ArchitectureFindingKind.EnforcementUnavailable, finding.Kind);
        Assert.Equal("enforcement-unavailable|hard-edge-policy", finding.Key);
    }

    [Fact]
    public void TheFindingNamesWhatWasRequiredAndWhatWasFound()
    {
        var result = factory.Create(Evaluation(new EnforcementReach(0, 4, 2, ["calls", "references"], ["declares", "using"])));
        var message = Assert.Single(result.Value).Message;

        // Without both halves the reader cannot tell whether the fix is configuration or tooling.
        Assert.Contains("calls, references", message, StringComparison.Ordinal);
        Assert.Contains("declares, using", message, StringComparison.Ordinal);
        Assert.Contains("archy doctor", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnforceableEvaluationProducesNoSuchFinding()
    {
        var result = factory.Create(Evaluation(new EnforcementReach(7, 12, 3, ["references"], ["references"])));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Empty(result.Value);
    }

    private static LayerDependencyEvaluation Evaluation(EnforcementReach reach) =>
        new([], [], [], [], reach);
}
