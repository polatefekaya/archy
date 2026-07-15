using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.ResolveDotNetMessageContracts;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.ResolveDotNetMessageContracts;

public sealed class DotNetMessageContractProviderTests
{
    [Fact]
    public async Task ResolveEmitsDistinctPublishAndSendEdgesForAnExplicitConsumerContract()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Messages.cs",
            """
            namespace Contracts
            {
                public interface IConsumer<TMessage> { }
            }

            namespace Sample
            {
                public sealed class OrderSubmitted { }
                public sealed class OrderConsumer : Contracts.IConsumer<OrderSubmitted> { }

                public sealed class OrderPublisher
                {
                    public void Deliver(object bus)
                    {
                        bus.Publish<OrderSubmitted>(new OrderSubmitted());
                        bus.Send<OrderSubmitted>(new OrderSubmitted());
                    }
                }
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetMessageContractProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Diagnostics);
        Assert.Equal(2, result.Value.Edges.Count);
        Assert.All(result.Value.Edges, static edge =>
        {
            Assert.Equal("dotnet-message-syntax", edge.Provider);
            Assert.Equal(0.8, edge.Confidence);
            Assert.Equal("OrderSubmitted", edge.NormalizedJoinKey);
            Assert.Equal("csharp:type:src/Messages.cs:Sample.OrderPublisher", edge.SourceStableId);
            Assert.Equal("csharp:type:src/Messages.cs:Sample.OrderConsumer", edge.TargetStableId);
        });
        Assert.Equal(
            ["message_publish", "message_send"],
            result.Value.Edges
                .Select(static edge => edge.EdgeKind)
                .OrderBy(static edgeKind => edgeKind, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ResolveDoesNotGuessAnEdgeWhenNoExplicitConsumerExists()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Producer.cs",
            """
            namespace Sample;

            public sealed class OrderSubmitted { }
            public sealed class OrderPublisher
            {
                public void Deliver(object bus) => bus.Publish<OrderSubmitted>(new OrderSubmitted());
            }
            """);
        var syntax = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);
        Assert.True(syntax.IsSuccess);

        var result = await new DotNetMessageContractProvider().ResolveAsync(
            fixture.RepositoryRoot,
            [file],
            syntax.Value.Nodes,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Edges);
        var diagnostic = Assert.Single(result.Value.Diagnostics);
        Assert.Equal("unresolved_message_consumer", diagnostic.Code);
        Assert.Contains("OrderSubmitted", diagnostic.Message, StringComparison.Ordinal);
    }
}
