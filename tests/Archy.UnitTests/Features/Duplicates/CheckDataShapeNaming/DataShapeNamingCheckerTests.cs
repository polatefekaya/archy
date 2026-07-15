using Archy.Features.Duplicates.CheckDataShapeNaming;

namespace Archy.UnitTests.Features.Duplicates.CheckDataShapeNaming;

public sealed class DataShapeNamingCheckerTests
{
    [Fact]
    public void CheckRoutesHighlySimilarDataShapesToANamingAdvisory()
    {
        var result = new DataShapeNamingChecker().Check(new DataShapeNamingComparison(
            new DataShapeNamingCandidate("record:a", "OrderDto", DataShapeKind.DataTransferObject),
            new DataShapeNamingCandidate("record:b", "OrderData", DataShapeKind.Record),
            .93d));

        var advisory = Assert.IsType<DataShapeNamingAdvisory>(result);
        Assert.Equal("record:a", advisory.LeftStableId);
        Assert.Equal("record:b", advisory.RightStableId);
        Assert.Contains("naming", advisory.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckDoesNotMakeANamingClaimForUnrelatedNames()
    {
        var result = new DataShapeNamingChecker().Check(new DataShapeNamingComparison(
            new DataShapeNamingCandidate("record:a", "OrderDto", DataShapeKind.DataTransferObject),
            new DataShapeNamingCandidate("record:b", "TelemetryEnvelope", DataShapeKind.Record),
            .95d));

        Assert.Null(result);
    }
}
