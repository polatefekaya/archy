using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;
using Archy.Features.Duplicates.ComposeDuplicateInspection;
using Archy.Features.Duplicates.DuplicateFindings;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Duplicates.ComposeDuplicateInspection;

public sealed class DuplicateInspectionComposerTests
{
    [Fact]
    public void ComposeIncludesAllSignalsAndSourceReferencesWithoutRawEmbeddingVectors()
    {
        var left = Node("method:left", "src/Left.cs", 4, 12);
        var right = Node("method:right", "src/Right.cs", 8, 16);
        var observation = new DuplicateFindingObservation("finding:pair", "observation:1", Target(left), Target(right), 3, "aggregate-v1", .91d, "{\"qualified\":true}",
            [new DuplicateSignalObservation("signal:semantic", DuplicateSignalKind.Semantic, .91d, "{\"rawCosine\":0.91,\"embedding\":[0.1,0.2],\"populationSize\":20}", DateTimeOffset.UtcNow)], DateTimeOffset.UtcNow);
        var graph = new GraphRevisionSnapshot(3, [left, right], [], [], []);

        var inspection = new DuplicateInspectionComposer(new DuplicateDecisionFeedbackPolicy()).Compose(new DuplicateInspectionRequest(observation, graph, []));

        Assert.Equal("src/Left.cs", inspection.Left.RepositoryRelativePath);
        Assert.Equal("src/Right.cs", inspection.Right.RepositoryRelativePath);
        var signal = Assert.Single(inspection.Signals);
        Assert.Contains("rawCosine", signal.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains("populationSize", signal.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("embedding", signal.EvidenceJson, StringComparison.OrdinalIgnoreCase);
    }

    private static ArchitectureTarget Target(GraphNodeFact node) => new(ArchitectureTargetKind.GraphNode, node.StableId);
    private static GraphNodeFact Node(string id, string path, int start, int end) => new(id, "method", id, id, path, start, end, "test", 1d, "{}", "hash");
}
