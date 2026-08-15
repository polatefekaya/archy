using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.ExplainReuseDecision;

namespace Archy.UnitTests.Features.Similarity.ExplainReuseDecision;

public sealed class ReuseDecisionExplainerTests
{
    [Fact]
    public void RecommendsReuseOnlyWithSignatureAndStructuralCorroboration()
    {
        var source = Node("source", "CreateSession", "src/Sessions/Source.cs"); var candidate = Node("candidate", "CreateSession", "src/Sessions/Candidate.cs");
        var snapshot = Snapshot(source, candidate, "void()", "void()");

        var result = new ReuseDecisionExplainer().Explain(snapshot, new(candidate.StableId, source.StableId, "create session"));

        Assert.Equal(ReuseRecommendation.Reuse, result.Recommendation);
        Assert.Contains(result.SupportingFactors, factor => factor.Id == "public_signature_match");
    }

    [Fact]
    public void RejectsReuseForIncompatiblePublicContract()
    {
        var source = Node("source", "CreateSession", "src/Sessions/Source.cs"); var candidate = Node("candidate", "CreateSession", "src/Sessions/Candidate.cs");
        var snapshot = Snapshot(source, candidate, "Task<string>()", "void()");

        var result = new ReuseDecisionExplainer().Explain(snapshot, new(candidate.StableId, source.StableId, "create session"));

        Assert.Equal(ReuseRecommendation.DoNotReuse, result.Recommendation);
        Assert.Contains(result.DifferentiatingFactors, factor => factor.Id == "public_signature_mismatch");
    }

    [Fact]
    public void AbstainsWhenOnlyIntentEvidenceExists()
    {
        var candidate = Node("candidate", "CreateSession", "src/Sessions/Candidate.cs");
        var snapshot = new GraphRevisionSnapshot(1, [candidate], [], [], []);

        var result = new ReuseDecisionExplainer().Explain(snapshot, new(candidate.StableId, null, "create session"));

        Assert.Equal(ReuseRecommendation.InsufficientEvidence, result.Recommendation);
    }

    private static GraphRevisionSnapshot Snapshot(GraphNodeFact source, GraphNodeFact candidate, string sourceSignature, string candidateSignature) => new(1, [source, candidate], [], [Symbol(source, sourceSignature), Symbol(candidate, candidateSignature)], []);
    private static GraphNodeFact Node(string id, string name, string path) => new(id, "method", name, name, path, 1, 1, "test", 1d, "{}", new string('a', 64));
    private static GraphSymbolFact Symbol(GraphNodeFact node, string signature) => new($"symbol:{node.StableId}", node.StableId, node.CanonicalKey, "public", signature, "[]", "void", "hash");
}
