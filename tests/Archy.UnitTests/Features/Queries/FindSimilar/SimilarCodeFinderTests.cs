using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Queries.FindSimilar;

namespace Archy.UnitTests.Features.Queries.FindSimilar;

public sealed class SimilarCodeFinderTests
{
    [Fact]
    public void RanksSymbolAndDependencyEvidenceDeterministically()
    {
        var source = Node("source", "CreateSession");
        var candidate = Node("candidate", "CreateArchitectureSession");
        var unrelated = Node("other", "RenderGraph");
        var snapshot = new GraphRevisionSnapshot(1, [source, candidate, unrelated], [Edge("source", "dependency"), Edge("candidate", "dependency")], [], []);

        var results = SimilarCodeFinder.Find(snapshot, new SimilarCodeQuery("create session", "source", 10));

        var first = results[0];
        Assert.Equal("candidate", first.StableId);
        Assert.Contains(first.Evidence, evidence => evidence.Kind == "symbol");
        Assert.Contains(first.Evidence, evidence => evidence.Kind == "dependency_neighborhood");
    }

    [Fact]
    public void RejectsUnboundedRequests()
    {
        var snapshot = new GraphRevisionSnapshot(1, [], [], [], []);
        Assert.Throws<ArgumentException>(() => SimilarCodeFinder.Find(snapshot, new SimilarCodeQuery("query", null, 51)));
    }

    private static GraphNodeFact Node(string id, string name) => new(id, "symbol", name, name, $"src/{name}.cs", null, null, "test", 1, "{}", "hash");
    private static GraphEdgeFact Edge(string source, string target) => new($"{source}-{target}", source, target, "calls", null, "test", 1, "{}");
}
