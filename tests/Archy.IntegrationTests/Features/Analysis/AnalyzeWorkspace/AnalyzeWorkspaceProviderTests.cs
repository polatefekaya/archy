using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.AnalyzeWorkspace;

public sealed class AnalyzeWorkspaceProviderTests
{
    [Fact]
    public async Task AnalyzePersistsAnExplicitDotNetDependencyInjectionEdge()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "Registration.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            namespace Sample;
            public interface IClock { }
            public sealed class SystemClock : IClock { }
            public sealed class TimeReporter
            {
                public TimeReporter(IClock clock) { }
            }
            public static class Registration
            {
                public static void Configure(object services) => services.AddScoped<IClock, SystemClock>();
            }
            """);

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler().Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        var registrations = Assert.IsType<Archy.Features.Analysis.ResolveDotNetDependencyRegistrations.DotNetDependencyRegistrationFacts>(
            result.Value.DependencyRegistrationFacts);
        var edge = Assert.Single(registrations.Edges);
        Assert.Equal("di_registration", edge.EdgeKind);
        Assert.Equal("AddScoped:IClock=>SystemClock", edge.NormalizedJoinKey);
        Assert.Equal(
            1L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_edges WHERE edge_kind = 'di_registration';"));
        var consumptions = Assert.IsType<Archy.Features.Analysis.ResolveDotNetDependencyConsumptions.DotNetDependencyConsumptionFacts>(
            result.Value.DependencyConsumptionFacts);
        var consumption = Assert.Single(consumptions.Edges);
        Assert.Equal("di_consumes", consumption.EdgeKind);
        Assert.Equal(
            1L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_edges WHERE edge_kind = 'di_consumes';"));
    }

    [Fact]
    public async Task AnalyzePersistsExplicitDotNetMessageContractEdges()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "Messages.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            namespace Contracts { public interface IConsumer<TMessage> { } }
            namespace Sample
            {
                public sealed class OrderSubmitted { }
                public sealed class OrderConsumer : Contracts.IConsumer<OrderSubmitted> { }
                public sealed class OrderPublisher
                {
                    public void Deliver(object bus) => bus.Publish<OrderSubmitted>(new OrderSubmitted());
                }
            }
            """);

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler().Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        var contracts = Assert.IsType<Archy.Features.Analysis.ResolveDotNetMessageContracts.DotNetMessageContractFacts>(
            result.Value.MessageContractFacts);
        var edge = Assert.Single(contracts.Edges);
        Assert.Equal("message_publish", edge.EdgeKind);
        Assert.Equal("OrderSubmitted", edge.NormalizedJoinKey);
        Assert.Equal(
            1L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_edges WHERE edge_kind = 'message_publish';"));
    }

    [Fact]
    public async Task AnalyzePersistsVirtualConfigurationKeysAndReadEdges()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "SettingsReader.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            namespace Sample;
            public sealed class SettingsReader
            {
                public string Read(object configuration) => configuration.GetValue<string>("ConnectionStrings:Primary");
            }
            """);

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler().Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        var reads = Assert.IsType<Archy.Features.Analysis.ResolveDotNetConfigurationReads.DotNetConfigurationReadFacts>(
            result.Value.ConfigurationReadFacts);
        var keyNode = Assert.Single(reads.Nodes);
        Assert.Equal("configuration:key:ConnectionStrings:Primary", keyNode.StableId);
        var edge = Assert.Single(reads.Edges);
        Assert.Equal("get_value:ConnectionStrings:Primary", edge.NormalizedJoinKey);
        Assert.Equal(
            1L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_nodes WHERE node_kind = 'configuration_key';"));
        Assert.Equal(
            1L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_edges WHERE edge_kind = 'configuration_read';"));
    }

    [Fact]
    public async Task AnalyzeLinksConfigurationReadsToJsonDefinitionFiles()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "SettingsReader.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            namespace Sample;
            public sealed class SettingsReader
            {
                public string Read(object configuration) => configuration.GetValue<string>("ConnectionStrings:Primary");
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Repository.Root, "appsettings.json"),
            """
            { "ConnectionStrings": { "Primary": "Data Source=archy.db" } }
            """);

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler().Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        var definitions = Assert.IsType<Archy.Features.Analysis.ResolveJsonConfigurationDefinitions.JsonConfigurationDefinitionFacts>(
            result.Value.ConfigurationDefinitionFacts);
        Assert.Equal(2, definitions.Edges.Count);
        Assert.Contains(definitions.Edges, static edge => edge.TargetStableId == "configuration:key:ConnectionStrings:Primary");
        Assert.Equal(
            2L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_edges WHERE edge_kind = 'configuration_defines';"));
        Assert.Equal(
            1L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_nodes WHERE node_kind = 'configuration_file';"));
    }

    [Fact]
    public async Task AnalyzePersistsLiteralRabbitMqTopology()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        var sourcePath = Path.Combine(fixture.Repository.Root, "src", "RabbitTopology.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(
            sourcePath,
            """
            namespace Sample;
            public sealed class Publisher
            {
                public void Publish(object channel) => channel.BasicPublish("orders", "order.created", false, null, []);
            }
            public sealed class Topology
            {
                public void Bind(object channel) => channel.QueueBind("orders-worker", "orders", "order.created");
            }
            public sealed class Consumer
            {
                public void Consume(object channel) => channel.BasicConsume("orders-worker", false, new object());
            }
            """);

        var result = await AnalyzeWorkspaceTestSupport.CreateHandler().Handle(
            new AnalyzeWorkspaceCommand(fixture.Repository.Root, ExplicitConfigurationPath: null, StateRootOverride: fixture.StateRoot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        var topology = Assert.IsType<Archy.Features.Analysis.ResolveRabbitMqTopology.RabbitMqTopologyFacts>(
            result.Value.RabbitMqTopologyFacts);
        Assert.Equal(3, topology.Edges.Count);
        Assert.Equal(
            1L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_edges WHERE edge_kind = 'rabbitmq_route';"));
        Assert.Equal(
            2L,
            await SqliteTestDatabase.ScalarLongAsync(
                initialized.Value.StateLocation.DatabasePath,
                "SELECT COUNT(*) FROM graph_nodes WHERE node_kind IN ('message_exchange', 'message_queue');"));
    }
}
