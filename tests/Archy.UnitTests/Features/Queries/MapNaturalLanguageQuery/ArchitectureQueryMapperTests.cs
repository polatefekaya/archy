using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Queries.MapNaturalLanguageQuery;

namespace Archy.UnitTests.Features.Queries.MapNaturalLanguageQuery;

public sealed class ArchitectureQueryMapperTests
{
    [Theory]
    [InlineData("what breaks if I delete type:orders?", "type:orders", GraphTraversalDirection.Dependents)]
    [InlineData("what uses column:customer-id?", "column:customer-id", GraphTraversalDirection.Dependents)]
    [InlineData("what does type:orders use?", "type:orders", GraphTraversalDirection.Dependencies)]
    public void MapsOnlyDocumentedGrammar(string query, string stableId, GraphTraversalDirection direction)
    {
        var result = ArchitectureQueryMapper.Map(query, 7);
        Assert.NotNull(result.Intent); Assert.Equal(stableId, result.Intent.StableId); Assert.Equal(direction, result.Intent.Direction); Assert.Equal(7, result.Intent.Revision);
    }
    [Fact] public void RequestsClarificationForUnsupportedPhrasing() { var result = ArchitectureQueryMapper.Map("tell me the architecture"); Assert.Null(result.Intent); Assert.NotNull(result.Clarification); }
}
