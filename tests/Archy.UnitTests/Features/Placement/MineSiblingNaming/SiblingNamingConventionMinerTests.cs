using Archy.Features.Placement.MineSiblingNaming;

namespace Archy.UnitTests.Features.Placement.MineSiblingNaming;

public sealed class SiblingNamingConventionMinerTests
{
    [Fact]
    public void SuggestUsesAConsistentSiblingSuffixAndMatchingFileConvention()
    {
        var result = new SiblingNamingConventionMiner().Suggest("Invoice", [Sample("OrderHandler"), Sample("PaymentHandler"), Sample("RefundHandler")]);

        Assert.Equal("InvoiceHandler", result!.SuggestedTypeName);
        Assert.Equal("InvoiceHandler.cs", result.SuggestedFileName);
    }

    [Fact]
    public void SuggestAbstainsForMixedSiblingConventions()
    {
        var result = new SiblingNamingConventionMiner().Suggest("Invoice", [Sample("OrderHandler"), Sample("PaymentService"), Sample("RefundHandler")]);

        Assert.Null(result);
    }

    private static SiblingNamingSample Sample(string typeName) => new($"src/Core/{typeName}.cs", typeName);
}
