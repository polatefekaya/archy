using Archy.Features.Duplicates.MapStructuralClonesToSymbols;
using Archy.Features.Duplicates.ParseJscpdCloneReport;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.UnitTests.Features.Duplicates.MapStructuralClonesToSymbols;

public sealed class StructuralCloneSymbolMapperTests
{
    private readonly StructuralCloneSymbolMapper mapper = new();

    [Fact]
    public void MapAttributesRangesToTheNarrowestExecutableSymbolsAndOrdersThePair()
    {
        var graph = Snapshot(
            Node("method:outer", "src/A.cs", 1, 30),
            Node("method:right", "src/B.cs", 10, 25),
            Node("method:left", "src/A.cs", 8, 16));
        var occurrence = Clone("src/B.cs", 12, 14, "src/A.cs", 10, 12);

        var result = mapper.Map(graph, [occurrence]);

        var pair = Assert.Single(result.Pairs);
        Assert.Equal("method:left", pair.LeftStableId);
        Assert.Equal("method:right", pair.RightStableId);
        Assert.Matches("^[0-9a-f]{64}$", pair.PairId);
        var evidence = Assert.Single(pair.Evidence);
        Assert.Equal("src/A.cs", evidence.LeftRange.RepositoryRelativePath);
        Assert.Equal("src/B.cs", evidence.RightRange.RepositoryRelativePath);
        Assert.Empty(result.Excluded);
    }

    [Theory]
    [InlineData("src/Generated/Clone.cs", "generated-source")]
    [InlineData("src/A.cs", "no-containing-executable-symbol")]
    public void MapExcludesEvidenceThatCannotSafelyBecomeSymbolEvidence(string path, string reason)
    {
        var graph = Snapshot(Node("method:right", "src/B.cs", 10, 25));

        var result = mapper.Map(graph, [Clone(path, 10, 12, "src/B.cs", 12, 14)]);

        var exclusion = Assert.Single(result.Excluded);
        Assert.Equal(reason, exclusion.Reason);
        Assert.Empty(result.Pairs);
    }

    [Fact]
    public void MapExcludesEqualNarrowestSpansAsAmbiguousInsteadOfPickingAStableIdArbitrarily()
    {
        var graph = Snapshot(Node("method:first", "src/A.cs", 1, 20), Node("method:second", "src/A.cs", 1, 20), Node("method:right", "src/B.cs", 1, 20));

        var result = mapper.Map(graph, [Clone("src/A.cs", 5, 8, "src/B.cs", 5, 8)]);

        Assert.Equal("ambiguous-executable-symbol", Assert.Single(result.Excluded).Reason);
        Assert.Empty(result.Pairs);
    }

    private static StructuralCloneOccurrence Clone(string firstPath, int firstStart, int firstEnd, string secondPath, int secondStart, int secondEnd) =>
        new(new CloneSourceRange(firstPath, firstStart, 1, firstEnd, 1), new CloneSourceRange(secondPath, secondStart, 1, secondEnd, 1), 42, 3, "csharp");

    private static GraphNodeFact Node(string stableId, string path, int startLine, int endLine) =>
        new(stableId, "semantic_method", stableId, stableId, path, startLine, endLine, "test", 1, "{}", "hash");

    private static GraphRevisionSnapshot Snapshot(params GraphNodeFact[] nodes) => new(1, nodes, [], [], []);
}
