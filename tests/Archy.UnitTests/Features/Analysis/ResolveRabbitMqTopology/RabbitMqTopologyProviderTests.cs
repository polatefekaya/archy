using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.ResolveRabbitMqTopology;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.ResolveRabbitMqTopology;

public sealed class RabbitMqTopologyProviderTests
{
    [Fact]
    public async Task ResolveCreatesTraversableLiteralPublishBindingAndConsumerTopology()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/RabbitTopology.cs",
            """
            namespace Sample;

            public sealed class Publisher
            {
                public void Publish(object channel) => channel.BasicPublish(
                    exchange: "orders",
                    routingKey: "order.created",
                    mandatory: false,
                    basicProperties: null,
                    body: []);
            }

            public sealed class Topology
            {
                public void Bind(object channel) => channel.QueueBind(
                    queue: "orders-worker",
                    exchange: "orders",
                    routingKey: "order.created");
            }

            public sealed class Consumer
            {
                public void Consume(object channel) => channel.BasicConsume("orders-worker", false, new object());
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new RabbitMqTopologyProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Diagnostics);
        Assert.Equal(2, result.Value.Nodes.Count);
        Assert.Contains(result.Value.Nodes, static node => node.StableId == "rabbitmq:exchange:orders");
        Assert.Contains(result.Value.Nodes, static node => node.StableId == "rabbitmq:queue:orders-worker");
        Assert.Equal(3, result.Value.Edges.Count);
        Assert.Contains(result.Value.Edges, static edge =>
            edge.EdgeKind == "rabbitmq_publish" &&
            edge.SourceStableId == "csharp:type:src/RabbitTopology.cs:Sample.Publisher" &&
            edge.TargetStableId == "rabbitmq:exchange:orders");
        Assert.Contains(result.Value.Edges, static edge =>
            edge.EdgeKind == "rabbitmq_route" &&
            edge.SourceStableId == "rabbitmq:exchange:orders" &&
            edge.TargetStableId == "rabbitmq:queue:orders-worker" &&
            edge.NormalizedJoinKey == "order.created");
        Assert.Contains(result.Value.Edges, static edge =>
            edge.EdgeKind == "rabbitmq_consume" &&
            edge.SourceStableId == "rabbitmq:queue:orders-worker" &&
            edge.TargetStableId == "csharp:type:src/RabbitTopology.cs:Sample.Consumer");
    }

    [Fact]
    public async Task ResolveUsesTheCanonicalDefaultExchangeForAnEmptyLiteralName()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/DefaultExchange.cs",
            """
            namespace Sample;
            public sealed class Publisher
            {
                public void Publish(object channel) => channel.BasicPublish("", "orders-worker", false, null, []);
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new RabbitMqTopologyProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var exchange = Assert.Single(result.Value.Nodes);
        Assert.Equal("rabbitmq:exchange:amq.default", exchange.StableId);
        Assert.Equal("amq.default", exchange.DisplayName);
    }

    [Fact]
    public async Task ResolveReportsDynamicRoutesWithoutCreatingTopologyFacts()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/DynamicRabbit.cs",
            """
            namespace Sample;
            public sealed class DynamicRabbit
            {
                public void Configure(object channel, string exchange, string queue, string routingKey)
                {
                    channel.BasicPublish(exchange, routingKey, false, null, []);
                    channel.QueueBind(queue, exchange, routingKey);
                    channel.BasicConsume(queue, false, new object());
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new RabbitMqTopologyProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Nodes);
        Assert.Empty(result.Value.Edges);
        Assert.Equal(3, result.Value.Diagnostics.Count);
        Assert.All(result.Value.Diagnostics, static diagnostic =>
            Assert.Equal("dynamic_rabbitmq_route", diagnostic.Code));
    }
}
