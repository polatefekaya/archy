using Archy.Features.Duplicates.CompareInterfaceSignatures;

namespace Archy.UnitTests.Features.Duplicates.CompareInterfaceSignatures;

public sealed class InterfaceSignatureSimilarityScorerTests
{
    [Fact]
    public void ScoreRetainsIndependentComponentsForSimilarAndDissimilarApis()
    {
        var scorer = new InterfaceSignatureSimilarityScorer();
        var similar = scorer.Score(new InterfaceSignature("left", "GetCustomerById", ["Guid"], "Customer", 0), new InterfaceSignature("right", "FindCustomerById", ["Guid"], "Customer", 0));
        var dissimilar = scorer.Score(new InterfaceSignature("left", "GetCustomerById", ["Guid"], "Customer", 0), new InterfaceSignature("other", "PublishBatch", ["byte[]", "CancellationToken"], "Task", 1));

        Assert.Equal(1, similar.ParameterTypeScore);
        Assert.Equal(1, similar.ReturnTypeScore);
        Assert.True(similar.OverallScore > dissimilar.OverallScore);
        Assert.Equal(0, dissimilar.GenericArityScore);
    }
}
