using Archy.Features.Duplicates.PartitionEmbeddingCandidates;
using Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

namespace Archy.UnitTests.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

public sealed class RelativeEmbeddingSimilarityScorerTests
{
    [Fact]
    public void ScoreRanksCosineWithinItsOwnBandAndIncludesPopulationCalibration()
    {
        var band = new DuplicateComparisonBand("csharp", DuplicateComplexityBand.Simple, DuplicateLineCountBand.Standard, ["method:a", "method:b", "method:c"]);
        var candidates = new[]
        {
            new EmbeddingSimilarityCandidate("method:a", [1f, 0f]),
            new EmbeddingSimilarityCandidate("method:b", [.9f, .1f]),
            new EmbeddingSimilarityCandidate("method:c", [0f, 1f]),
        };

        var results = new RelativeEmbeddingSimilarityScorer().Score(band, candidates);

        Assert.Equal(3, results.Count);
        var strongest = results[0];
        Assert.Equal("method:a", strongest.LeftMethodStableId);
        Assert.Equal("method:b", strongest.RightMethodStableId);
        Assert.True(strongest.RawCosineSimilarity > results[1].RawCosineSimilarity);
        Assert.True(strongest.ZScore > 0d);
        Assert.Equal(3, strongest.CandidatePopulationSize);
        Assert.Equal(3, strongest.PairPopulationSize);
        Assert.True(strongest.PopulationStandardDeviation > 0d);
    }
}
