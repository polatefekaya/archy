using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.BuildSimilarityClusters;

namespace Archy.UnitTests.Features.Similarity.BuildSimilarityClusters;

public sealed class SimilarityClusterBuilderTests
{
    [Fact]
    public void BuildsDeterministicMultiMemberClustersFromCorroboratedEvidence()
    {
        var first = Node("first", "CreateSession", "src/Sessions/First.cs"); var second = Node("second", "CreateSession", "src/Sessions/Second.cs");
        var snapshot = new GraphRevisionSnapshot(3, [first, second], [Edge("first", "shared"), Edge("second", "shared")], [], []);

        var result = SimilarityClusterBuilder.Build(snapshot);

        var cluster = Assert.Single(result.Clusters); Assert.Equal(["first", "second"], cluster.Members.Select(member => member.StableId));
        Assert.Equal($"{SimilarityClusterBuilder.AlgorithmVersion}:hybrid-structural/v1", result.AlgorithmVersion); Assert.Equal(1, result.ComparedCandidatePairs); Assert.Equal(2, result.EvaluatedCandidatePairs);
    }

    [Fact]
    public void ExcludesGeneratedAndSingleMemberComponents()
    {
        var generated = Node("generated", "CreateSession", "obj/CreateSession.g.cs"); var standalone = Node("standalone", "RenderGraph", "src/RenderGraph.cs");
        var result = SimilarityClusterBuilder.Build(new GraphRevisionSnapshot(1, [generated, standalone], [], [], []));
        Assert.Empty(result.Clusters); Assert.Equal(0, result.ComparedCandidatePairs);
    }

    [Fact]
    public void IncludesTheConfiguredPolicyVersionInPersistedAlgorithmIdentityAndInputHash()
    {
        var first = Node("first", "CreateSession", "src/Sessions/First.cs"); var second = Node("second", "CreateSession", "src/Sessions/Second.cs");
        var snapshot = new GraphRevisionSnapshot(3, [first, second], [Edge("first", "shared"), Edge("second", "shared")], [], []);
        var policy = new Archy.Features.Similarity.RetrieveHybridCandidates.HybridSimilarityPolicy("repo-policy/v2", new(.25d, .25d, .15d, .15d, .10d, .10d));

        var result = SimilarityClusterBuilder.Build(snapshot, policy);

        Assert.Equal("1:repo-policy/v2", result.AlgorithmVersion);
        Assert.NotEqual(SimilarityClusterBuilder.Build(snapshot).InputHash, result.InputHash);
    }

    [Fact]
    public void KeepsCandidateEvaluationBoundedForARepresentativeTwoThousandNodeCorpus()
    {
        var nodes = Enumerable.Range(0, 2_000)
            .Select(index => Node($"node:{index:D4}", $"HandleRequest{index:D4}", $"src/Features/Feature{index:D4}.cs"))
            .ToArray();

        var result = SimilarityClusterBuilder.Build(new GraphRevisionSnapshot(1, nodes, [], [], []));

        // The builder compares only its deterministic 500-node corpus, never all 2,000 nodes per seed.
        Assert.InRange(result.EvaluatedCandidatePairs, 0, 500 * 499);
        Assert.True(result.EvaluatedCandidatePairs > 0);
    }

    private static GraphNodeFact Node(string id, string name, string path) => new(id, "method", name, name, path, 1, 1, "test", 1d, "{}", new string('a', 64));
    private static GraphEdgeFact Edge(string source, string target) => new($"{source}-{target}", source, target, "calls", null, "test", 1d, "{}");
}
