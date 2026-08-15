using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.TraverseDependencies;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Planning.AnalyzeImpact;

public sealed class ImpactAnalyzerTests
{
    [Fact]
    public async Task SeparatesDirectAndTransitiveDependentsAndReportsPublicBlastRadius()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var target = GraphRevisionTestBuilder.Node("target", "aaaaaaaaaaaaaaaa"); var direct = GraphRevisionTestBuilder.Node("direct", "bbbbbbbbbbbbbbbb"); var transitive = GraphRevisionTestBuilder.Node("transitive", "cccccccccccccccc");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [target, direct, transitive], [GraphRevisionTestBuilder.Edge("direct-target", "direct", "target"), GraphRevisionTestBuilder.Edge("transitive-direct", "transitive", "direct")], [new("symbol:direct", "direct", "Direct", "public", "M()", "[]", "{}", "hash")]);
        var analyzer = new ImpactAnalyzer(new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System)), new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)));

        var result = await analyzer.AnalyzeAsync(initialized.Value.StateLocation, new("target", ImpactDirection.Dependents, 3, 100), CancellationToken.None);

        Assert.True(result.IsSuccess); Assert.Equal(["direct"], result.Value!.Direct.Select(path => path.StableId)); Assert.Equal(["transitive"], result.Value.Transitive.Select(path => path.StableId)); Assert.Equal(["direct"], result.Value.PublicSurfaceStableIds); Assert.Contains(result.Value.Risks, risk => risk.Id == "public_api");
    }

    [Fact]
    public async Task ClassifiesConfigurationMessageAndServiceRegistrationEffectsFromPersistedEdgeKinds()
    {
        using var fixture = WorkspaceStateFixture.Create(); var initialized = await fixture.InitializeAsync(); Assert.True(initialized.IsSuccess);
        var target = GraphRevisionTestBuilder.Node("target", "aaaaaaaaaaaaaaaa"); var configuration = GraphRevisionTestBuilder.Node("configuration", "bbbbbbbbbbbbbbbb"); var message = GraphRevisionTestBuilder.Node("message", "cccccccccccccccc"); var service = GraphRevisionTestBuilder.Node("service", "dddddddddddddddd");
        await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation, [target, configuration, message, service], [
            new("config-target", configuration.StableId, target.StableId, "configuration_reads", "config", "fixture", 1, "{}"),
            new("message-target", message.StableId, target.StableId, "message_publish", "message", "fixture", 1, "{}"),
            new("service-target", service.StableId, target.StableId, "di_registration", "service", "fixture", 1, "{}")]);
        var analyzer = new ImpactAnalyzer(new GraphTraversalReader(new WorkspaceLockManager(TimeProvider.System)), new GraphRevisionSnapshotReader(new WorkspaceLockManager(TimeProvider.System)));
        var result = await analyzer.AnalyzeAsync(initialized.Value.StateLocation, new(target.StableId, ImpactDirection.Dependents), CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Contains(result.Value!.Risks, risk => risk.Id == "configuration_contract"); Assert.Contains(result.Value.Risks, risk => risk.Id == "message_contract"); Assert.Contains(result.Value.Risks, risk => risk.Id == "service_registration");
    }
}
