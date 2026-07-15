using Archy.Features.Duplicates.PartitionEmbeddingCandidates;

namespace Archy.UnitTests.Features.Duplicates.PartitionEmbeddingCandidates;

public sealed class EmbeddingCandidatePartitionerTests
{
    [Fact]
    public void PartitionKeepsOnlySameLanguageAndMetricBandsAndRoutesDataShapesAway()
    {
        var candidates = new[]
        {
            Candidate("method:a", "csharp", 12, 3),
            Candidate("method:b", "csharp", 20, 2),
            Candidate("method:typescript", "typescript", 20, 2),
            Candidate("method:complex", "csharp", 80, 12),
            Candidate("method:dto", "csharp", 5, 1, isDataShape: true),
        };

        var plan = new EmbeddingCandidatePartitioner().Partition(candidates);

        var band = Assert.Single(plan.ComparableBands);
        Assert.Equal("csharp", band.Language);
        Assert.Equal(DuplicateComplexityBand.Simple, band.Complexity);
        Assert.Equal(DuplicateLineCountBand.Standard, band.LineCount);
        Assert.Equal(["method:a", "method:b"], band.MethodStableIds);
        var excluded = Assert.Single(plan.Excluded);
        Assert.Equal("method:dto", excluded.MethodStableId);
        Assert.Equal("data-shape", excluded.Reason);
    }

    private static DuplicateLogicCandidate Candidate(string id, string language, int lines, int complexity, bool isDataShape = false) =>
        new(id, language, lines, complexity, isDataShape);
}
