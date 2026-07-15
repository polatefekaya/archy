using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.InventorySources;

namespace Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

public sealed class CSharpSyntaxFactExtractorTests
{
    [Fact]
    public async Task ExtractProducesDeterministicDeclarationUsingAndPublicSurfaceFacts()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Domain/Order.cs",
            """
            using System;
            using System.Threading.Tasks;

            namespace Example.Domain;

            public interface IOrderService
            {
                Task<Order> GetAsync(Guid id);
            }

            public sealed class Order : IEquatable<Order>
            {
                public string Id { get; init; } = string.Empty;
            }
            """);

        var result = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        Assert.Empty(result.Value.Diagnostics);
        Assert.Contains(result.Value.Nodes, static node => node.StableId == "file:src/Domain/Order.cs");
        Assert.Contains(
            result.Value.Nodes,
            static node => node is
            {
                StableId: "csharp:type:src/Domain/Order.cs:Example.Domain.IOrderService",
                NodeKind: "interface",
                StartLine: 6,
            });
        Assert.Contains(
            result.Value.Nodes,
            static node => node.StableId == "csharp:using-reference:System.Threading.Tasks" &&
                           node.NodeKind == "unresolved_reference");
        Assert.Contains(
            result.Value.Edges,
            static edge => edge is
            {
                SourceStableId: "file:src/Domain/Order.cs",
                TargetStableId: "csharp:using-reference:System",
                EdgeKind: "using",
                Confidence: 0.65,
            });
        Assert.Contains(
            result.Value.Edges,
            static edge => edge is
            {
                SourceStableId: "file:src/Domain/Order.cs",
                TargetStableId: "csharp:type:src/Domain/Order.cs:Example.Domain.Order",
                EdgeKind: "declares",
                Confidence: 1,
            });
        Assert.Equal(
            [
                "Example.Domain.IOrderService",
                "Example.Domain.Order",
            ],
            result.Value.Symbols.Select(static symbol => symbol.FullyQualifiedName));
        var serviceFingerprint = Assert.Single(result.Value.InterfaceFingerprints, static fingerprint =>
            fingerprint.SymbolId == "csharp:symbol:src/Domain/Order.cs:Example.Domain.IOrderService");
        Assert.Equal("public-surface-v1", serviceFingerprint.FingerprintKind);
        Assert.Contains("GetAsync", serviceFingerprint.NormalizedMembersJson, StringComparison.Ordinal);
        Assert.Matches("^[A-F0-9]{64}$", serviceFingerprint.FingerprintHash);
    }

    [Fact]
    public async Task ExtractMarksSyntaxErrorsIncompleteButRetainsDiagnostics()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync(
            "src/Broken.cs",
            "namespace Example; public sealed class Broken {");

        var result = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsComplete);
        var diagnostic = Assert.Single(result.Value.Diagnostics);
        Assert.StartsWith("CS", diagnostic.Code, StringComparison.Ordinal);
        Assert.Equal("error", diagnostic.Severity);
        Assert.Equal("src/Broken.cs", diagnostic.RepositoryRelativePath);
    }

    [Fact]
    public async Task ExtractRejectsFilesChangedAfterInventoryAndPathsOutsideTheRepository()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var actualFile = await fixture.WriteAsync(
            "src/Current.cs",
            "namespace Example; public sealed class Current { }");
        var stale = actualFile with { ContentHash = new string('0', 64) };

        var staleResult = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [stale],
            CancellationToken.None);
        var escapedResult = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [new SourceFile("../outside.cs", SourceLanguage.CSharp, actualFile.ContentHash, actualFile.ByteLength)],
            CancellationToken.None);

        Assert.False(staleResult.IsSuccess);
        Assert.Equal("conflict", staleResult.Problem!.Code);
        Assert.False(escapedResult.IsSuccess);
        Assert.Equal("validation", escapedResult.Problem!.Code);
    }

    [Fact]
    public async Task ExtractIgnoresNonCSharpInventoryFiles()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        var file = await fixture.WriteAsync("docs/readme.md", "# Archy", SourceLanguage.Unknown);

        var result = await new CSharpSyntaxFactExtractor().ExtractAsync(
            fixture.RepositoryRoot,
            [file],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        Assert.Empty(result.Value.Nodes);
        Assert.Empty(result.Value.Edges);
        Assert.Empty(result.Value.Symbols);
        Assert.Empty(result.Value.InterfaceFingerprints);
        Assert.Empty(result.Value.Diagnostics);
    }
}
