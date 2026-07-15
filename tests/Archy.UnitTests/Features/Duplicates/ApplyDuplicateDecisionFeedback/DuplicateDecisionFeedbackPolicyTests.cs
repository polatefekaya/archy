using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Duplicates.ApplyDuplicateDecisionFeedback;

public sealed class DuplicateDecisionFeedbackPolicyTests
{
    [Fact]
    public void EvaluateSuppressesOnlyTheExactFindingWhoseLatestReviewWasIgnored()
    {
        var history = new[]
        {
            Decision("decision:other", "finding:other", DecisionResolution.Ignored, "2026-07-15T10:00:00+00:00"),
            Decision("decision:accepted", "finding:target", DecisionResolution.Accepted, "2026-07-15T11:00:00+00:00"),
            Decision("decision:ignored", "finding:target", DecisionResolution.Ignored, "2026-07-15T12:00:00+00:00"),
        };

        var feedback = new DuplicateDecisionFeedbackPolicy().Evaluate("finding:target", history);

        Assert.True(feedback.SuppressRepeatedFinding);
        Assert.Equal(DecisionResolution.Ignored, feedback.LatestResolution);
        Assert.Equal(-.025d, feedback.ConfidenceAdjustment);
        Assert.Equal(["decision:ignored", "decision:accepted"], feedback.DecisionIds);
    }

    [Fact]
    public void CreateProducesOneDurableExactFindingTarget()
    {
        var fact = DuplicateReviewDecisionFactory.Create(new DuplicateReviewDecision("finding:target", DecisionResolution.Modified, "Reworded", "user", "kariyer", null, 9));

        Assert.Equal(DuplicateReviewDecisionFactory.DecisionType, fact.DecisionType);
        var target = Assert.Single(fact.Targets);
        Assert.Equal(ArchitectureTargetKind.DuplicateFinding, target.Kind);
        Assert.Equal("finding:target", target.StableId);
    }

    private static ArchitectureDecision Decision(string id, string findingId, DecisionResolution resolution, string occurredAt) =>
        new(id, DuplicateReviewDecisionFactory.DecisionType, resolution, null, "user", "fixture", null, 1,
            [new ArchitectureTarget(ArchitectureTargetKind.DuplicateFinding, findingId)],
            DateTimeOffset.Parse(occurredAt, System.Globalization.CultureInfo.InvariantCulture));
}
