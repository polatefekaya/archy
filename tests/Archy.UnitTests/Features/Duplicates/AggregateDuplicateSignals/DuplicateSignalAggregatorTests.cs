using Archy.Features.Duplicates.AggregateDuplicateSignals;
using Archy.Features.Duplicates.DuplicateFindings;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Duplicates.AggregateDuplicateSignals;

public sealed class DuplicateSignalAggregatorTests
{
    public static TheoryData<bool, bool, bool, bool> SignalCombinations => new()
    {
        { false, false, false, false },
        { true, false, false, false },
        { false, true, false, false },
        { false, false, true, false },
        { true, true, false, true },
        { true, false, true, true },
        { false, true, true, true },
        { true, true, true, true },
    };

    [Theory]
    [MemberData(nameof(SignalCombinations))]
    public void AggregateRequiresAtLeastTwoIndependentQualifiedSignals(bool structural, bool signature, bool semantic, bool expectedFinding)
    {
        var signals = new List<DuplicateSignalFact>();
        if (structural) signals.Add(Signal(DuplicateSignalKind.Structural, .80d));
        if (signature) signals.Add(Signal(DuplicateSignalKind.Signature, .85d));
        if (semantic) signals.Add(Signal(DuplicateSignalKind.Semantic, .90d));
        if (signals.Count == 0) signals.Add(Signal(DuplicateSignalKind.Structural, .20d));

        var result = new DuplicateSignalAggregator(DuplicateSignalThresholds.Default).Aggregate(new DuplicateSignalSet(Target("method:z"), Target("method:a"), 7, signals));

        Assert.Equal(expectedFinding, result.IsLikelyDuplicate);
        Assert.Equal(expectedFinding, result.Finding is not null);
        Assert.Contains("qualifiedSignals", result.RationaleJson, StringComparison.Ordinal);
        if (result.Finding is not null)
        {
            Assert.Equal("method:a", result.Finding.LeftTarget.StableId);
            Assert.Equal("method:z", result.Finding.RightTarget.StableId);
            Assert.Equal(DuplicateSignalAggregator.AggregationVersion, result.Finding.AggregationVersion);
            Assert.Equal(signals.Count, result.Finding.Signals.Count);
        }
    }

    private static ArchitectureTarget Target(string stableId) => new(ArchitectureTargetKind.GraphNode, stableId);
    private static DuplicateSignalFact Signal(DuplicateSignalKind kind, double score) => new(kind, score, "{\"source\":\"test\"}");
}
