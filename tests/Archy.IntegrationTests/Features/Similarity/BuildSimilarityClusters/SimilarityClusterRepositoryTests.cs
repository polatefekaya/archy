using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Integrations.Mcp.RunMcpServer;
using Archy.Features.Similarity.BuildSimilarityClusters;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Similarity.BuildSimilarityClusters;

public sealed class SimilarityClusterRepositoryTests
{
    [Fact]
    public async Task PersistsAndReadsImmutableRevisionScopedSimilarityClusters()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var first = GraphRevisionTestBuilder.Node("first", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/First.cs" };
        var second = GraphRevisionTestBuilder.Node("second", "bbbbbbbbbbbbbbbb") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/Second.cs" };
        var shared = GraphRevisionTestBuilder.Node("shared", "cccccccccccccccc");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [first, second, shared], [GraphRevisionTestBuilder.Edge("first-shared", "first", "shared"), GraphRevisionTestBuilder.Edge("second-shared", "second", "shared")]);
        var snapshot = await new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)).ReadActiveAsync(initialized.Value.StateLocation, CancellationToken.None); Assert.True(snapshot.IsSuccess);
        var build = SimilarityClusterBuilder.Build(snapshot.Value!); var repository = new SimilarityClusterRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System));

        var stored = await repository.RecordAsync(initialized.Value.StateLocation, build, CancellationToken.None);
        var latest = await repository.ReadLatestAsync(initialized.Value.StateLocation, build.GraphRevision, CancellationToken.None);

        Assert.True(stored.IsSuccess); Assert.True(latest.IsSuccess); Assert.NotNull(latest.Value); Assert.Equal(stored.Value.Id, latest.Value!.Id); Assert.Single(latest.Value.Clusters); Assert.Equal(["first", "second"], latest.Value.Clusters[0].Members.Select(member => member.StableId));
        using var arguments = System.Text.Json.JsonDocument.Parse($"{{\"clusterId\":\"{latest.Value.Clusters[0].Id}\"}}");
        var response = await new GetSimilarityClusterMcpTool().ExecuteAsync(new("get_similarity_cluster", arguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);
        Assert.True(response.IsSuccess); using var json = System.Text.Json.JsonDocument.Parse(response.ResultJson!); Assert.False(json.RootElement.GetProperty("structuredContent").GetProperty("abstention").GetBoolean());
        var duplicate = await repository.RecordAsync(initialized.Value.StateLocation, build, CancellationToken.None); Assert.False(duplicate.IsSuccess); Assert.Equal("conflict", duplicate.Problem!.Code);
    }

    [Fact]
    public async Task MarksAFormerClusterStaleAfterTheActiveGraphRevisionChangesWhileKeepingHistoryQueryable()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var first = GraphRevisionTestBuilder.Node("first", "aaaaaaaaaaaaaaaa") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/First.cs" };
        var second = GraphRevisionTestBuilder.Node("second", "bbbbbbbbbbbbbbbb") with { DisplayName = "CreateSession", CanonicalKey = "CreateSession", FilePath = "src/Sessions/Second.cs" };
        var shared = GraphRevisionTestBuilder.Node("shared", "cccccccccccccccc");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [first, second, shared], [GraphRevisionTestBuilder.Edge("first-shared", "first", "shared"), GraphRevisionTestBuilder.Edge("second-shared", "second", "shared")]);
        var reader = new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)); var firstSnapshot = await reader.ReadActiveAsync(initialized.Value.StateLocation, CancellationToken.None);
        var repository = new SimilarityClusterRepository(TimeProvider.System, new WorkspaceLockManager(TimeProvider.System)); var stored = await repository.RecordAsync(initialized.Value.StateLocation, SimilarityClusterBuilder.Build(firstSnapshot.Value!), CancellationToken.None); Assert.True(stored.IsSuccess);
        var id = stored.Value.Clusters.Single().Id;

        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [first, shared], [GraphRevisionTestBuilder.Edge("first-shared", "first", "shared")]);
        using var currentArguments = System.Text.Json.JsonDocument.Parse($"{{\"clusterId\":\"{id}\"}}");
        var tool = new GetSimilarityClusterMcpTool(); var current = await tool.ExecuteAsync(new("get_similarity_cluster", currentArguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);

        Assert.True(current.IsSuccess); using var currentJson = System.Text.Json.JsonDocument.Parse(current.ResultJson!); var currentContent = currentJson.RootElement.GetProperty("structuredContent"); Assert.True(currentContent.GetProperty("abstention").GetBoolean()); Assert.True(currentContent.GetProperty("stale").GetBoolean());
        using var historicalArguments = System.Text.Json.JsonDocument.Parse($"{{\"clusterId\":\"{id}\",\"graphRevision\":{firstSnapshot.Value!.Revision}}}");
        var historical = await tool.ExecuteAsync(new("get_similarity_cluster", historicalArguments.RootElement.Clone(), new(fixture.Repository.Root, initialized.Value.StateLocation)), CancellationToken.None);
        Assert.True(historical.IsSuccess); using var historicalJson = System.Text.Json.JsonDocument.Parse(historical.ResultJson!); var historicalContent = historicalJson.RootElement.GetProperty("structuredContent"); Assert.False(historicalContent.GetProperty("abstention").GetBoolean()); Assert.True(historicalContent.GetProperty("historical").GetBoolean());
    }
}
