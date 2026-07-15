using Archy.Features.Duplicates.RetrieveEmbeddingCandidates;
using Archy.Features.Duplicates.ScoreRelativeEmbeddingSimilarity;

namespace Archy.UnitTests.Features.Duplicates.RetrieveEmbeddingCandidates;

public sealed class BoundedEmbeddingCandidateRetrieverTests
{
    [Fact]
    public void RetrieveCapsExactVectorCandidatesPerMethodAndKeepsTheNearestFingerprintPair()
    {
        var candidates = Enumerable.Range(0, 20).Select(index => new EmbeddingSimilarityCandidate($"method:{index:D2}", [index == 0 ? 1f : 0f, index == 1 ? 1f : index / 20f])).ToArray();

        var result = new BoundedEmbeddingCandidateRetriever(new EmbeddingRetrievalOptions(2)).Retrieve(4, candidates);

        Assert.Equal(4, result.GraphRevision);
        Assert.Equal(20, result.CorpusSize);
        Assert.True(result.Pairs.Count <= 20);
        Assert.Contains(result.Pairs, pair => pair.LeftMethodStableId == "method:00" && pair.RightMethodStableId == "method:01");
    }
}
