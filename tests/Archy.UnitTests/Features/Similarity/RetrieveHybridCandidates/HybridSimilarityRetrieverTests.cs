using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.RetrieveHybridCandidates;

namespace Archy.UnitTests.Features.Similarity.RetrieveHybridCandidates;

public sealed class HybridSimilarityRetrieverTests
{
    [Fact]
    public void CombinesAvailableStructuralEvidenceAndDoesNotPenalizeUnavailableEmbeddings()
    {
        var source = Node("source", "CreateSession", "src/Sessions/CreateSession.cs");
        var candidate = Node("candidate", "CreateArchitectureSession", "src/Sessions/CreateArchitectureSession.cs");
        var snapshot = new GraphRevisionSnapshot(7, [source, candidate], [Edge("source", "dependency"), Edge("candidate", "dependency")], [Symbol(source), Symbol(candidate)], []);

        var result = new HybridSimilarityRetriever().Retrieve(snapshot, new("create session", "source", null));

        var match = Assert.Single(result.Candidates);
        Assert.Equal(HybridSimilarityPolicy.Default.Version, result.PolicyVersion);
        Assert.True(match.Score > 0d);
        Assert.Contains(match.Evidence, evidence => evidence.Kind == SimilarityEvidenceKind.Embedding && !evidence.IsAvailable && evidence.NormalizedContribution == 0d);
        Assert.Equal(match.Score, Math.Round(match.Evidence.Sum(evidence => evidence.NormalizedContribution), 6));
        Assert.Contains(match.Evidence, evidence => evidence.Kind == SimilarityEvidenceKind.DependencyNeighborhood && evidence.Detail.Contains("outgoing", StringComparison.Ordinal));
    }

    [Fact]
    public void UsesStableTieBreakerAfterScoreAndConfidence()
    {
        var source = Node("source", "Create", "src/Create.cs");
        var alpha = Node("alpha", "Create", "src/Alpha.cs");
        var beta = Node("beta", "Create", "src/Beta.cs");
        var snapshot = new GraphRevisionSnapshot(1, [source, beta, alpha], [], [], []);

        var result = new HybridSimilarityRetriever().Retrieve(snapshot, new("create", "source", null));

        Assert.Equal(["alpha", "beta"], result.Candidates.Select(candidate => candidate.StableId));
    }

    [Fact]
    public void AbstainsForUnknownSourceAndRejectsInvalidWeights()
    {
        var snapshot = new GraphRevisionSnapshot(1, [Node("known", "Known", "src/Known.cs")], [], [], []);
        var retriever = new HybridSimilarityRetriever();

        var absent = retriever.Retrieve(snapshot, new("known", "missing", null));

        Assert.True(absent.Abstained);
        Assert.Throws<ArgumentException>(() => retriever.Retrieve(snapshot, new("known", null, null, Weights: new(.5d, .5d, .5d, 0d, 0d, 0d))));
        Assert.Throws<ArgumentException>(() => retriever.Retrieve(snapshot, new("known", null, null, PolicyVersion: "")));
    }

    [Fact]
    public void AddsConfiguredLayerEvidenceOnlyWhenBothDeclarationsHaveDeterministicMembership()
    {
        var source = Node("source", "CreateSession", "src/Application/CreateSession.cs"); var candidate = Node("candidate", "CreateSession", "src/Application/CreateSessionHandler.cs");
        var snapshot = new GraphRevisionSnapshot(1, [source, candidate], [], [], []);

        var result = new HybridSimilarityRetriever().Retrieve(snapshot, new("create session", source.StableId, null, LayerNamesByStableId: new Dictionary<string, string> { [source.StableId] = "Application", [candidate.StableId] = "Application" }));

        var module = Assert.Single(Assert.Single(result.Candidates).Evidence, evidence => evidence.Kind == SimilarityEvidenceKind.ModuleContext); Assert.True(module.IsAvailable); Assert.Equal(1d, module.RawScore); Assert.Contains("Application", module.Detail, StringComparison.Ordinal);
    }

    private static GraphNodeFact Node(string id, string name, string path) => new(id, "method", name, name, path, 1, 1, "test", 1d, "{}", new string('a', 64));
    private static GraphSymbolFact Symbol(GraphNodeFact node) => new($"symbol:{node.StableId}", node.StableId, node.CanonicalKey, "public", "void()", "[]", "void", "hash");
    private static GraphEdgeFact Edge(string source, string target) => new($"{source}-{target}", source, target, "calls", null, "test", 1d, "{}");
}
