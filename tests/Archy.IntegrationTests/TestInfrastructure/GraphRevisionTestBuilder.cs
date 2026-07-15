using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class GraphRevisionTestBuilder
{
    public static async Task<long> CommitAsync(
        WorkspaceStateLocation location,
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact>? edges = null,
        IReadOnlyList<GraphSymbolFact>? symbols = null,
        IReadOnlyList<InterfaceFingerprintFact>? interfaceFingerprints = null)
    {
        var run = await new AnalysisRunRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).StartAsync(
            location,
            "archy-test",
            "configuration-hash",
            repositoryCommit: "deadbeef",
            CancellationToken.None);
        Assert.True(run.IsSuccess);

        var committed = await new GraphRevisionCommitter(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)).CommitAsync(
            location,
            run.Value.RunId,
            nodes,
            edges ?? [],
            symbols ?? [],
            interfaceFingerprints ?? [],
            CancellationToken.None);
        Assert.True(committed.IsSuccess);
        return committed.Value.Revision;
    }

    public static GraphNodeFact Node(string stableId, string contentHash) => new(
        stableId,
        "method",
        stableId,
        stableId,
        $"{stableId.Replace(':', '-')}.cs",
        1,
        10,
        "fixture",
        1,
        "{}",
        contentHash);

    public static GraphEdgeFact Edge(string edgeId, string sourceStableId, string targetStableId) => new(
        edgeId,
        sourceStableId,
        targetStableId,
        "calls",
        $"{sourceStableId}->{targetStableId}",
        "fixture",
        1,
        "{}");
}
