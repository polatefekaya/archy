using System.Diagnostics;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Graph;

/// <summary>
/// A deliberately modest regression gate. Full release benchmarks use the documented 10k/100k/1m LOC fixtures;
/// this test catches accidental unbounded traversal behavior in every ordinary integration run.
/// </summary>
public sealed class GraphTraversalPerformanceTests
{
    [Fact]
    public async Task BoundedTraversalKeepsP95BelowTheLocalRegressionBudget()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var nodes = Enumerable.Range(0, 512).Select(index => GraphRevisionTestBuilder.Node($"node:perf:{index:D4}", $"hash:{index}")).ToArray();
        var edges = Enumerable.Range(0, nodes.Length - 1)
            .Select(index => GraphRevisionTestBuilder.Edge($"edge:perf:{index:D4}", nodes[index].StableId, nodes[index + 1].StableId))
            .ToArray();
        var revision = await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, nodes, edges);
        var reader = new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System));
        var elapsed = new List<TimeSpan>();

        for (var sample = 0; sample < 25; sample++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await reader.TraverseAsync(initialized.Value.StateLocation, new GraphTraversalQuery(
                nodes[sample * 10].StableId,
                GraphTraversalDirection.Dependencies,
                revision,
                MaxDepth: 6,
                MaxEdges: 64), CancellationToken.None);
            stopwatch.Stop();
            Assert.True(result.IsSuccess);
            Assert.InRange(result.Value!.Edges.Count, 1, 6);
            elapsed.Add(stopwatch.Elapsed);
        }

        var p95 = elapsed.OrderBy(value => value).ElementAt((int)Math.Ceiling(elapsed.Count * .95) - 1);
        Assert.True(p95 < TimeSpan.FromSeconds(1), $"Traversal p95 was {p95.TotalMilliseconds:F0} ms; the local regression budget is 1000 ms.");
    }
}
