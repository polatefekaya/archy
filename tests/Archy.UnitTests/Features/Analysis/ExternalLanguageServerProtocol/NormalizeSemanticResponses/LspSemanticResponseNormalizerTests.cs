using System.Text.Json;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.NormalizeSemanticResponses;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Analysis.LanguageServerProfiles;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.UnitTests.Features.Analysis.ExternalLanguageServerProtocol.NormalizeSemanticResponses;

public sealed class LspSemanticResponseNormalizerTests
{
    private static readonly ConfiguredLspSemanticSymbolIdentityPolicy CSharpIdentity = new(CSharpProfile());

    [Fact]
    public void ExtractsHierarchicalSymbolsWithAdapterDefinedIdentityAndOneBasedRanges()
    {
        using var response = JsonDocument.Parse("""
            [{"name":"Acme","kind":3,"range":{"start":{"line":0,"character":0},"end":{"line":5,"character":4}},"selectionRange":{"start":{"line":0,"character":0},"end":{"line":5,"character":4}},"children":[{"name":"Clock","kind":5,"range":{"start":{"line":1,"character":0},"end":{"line":5,"character":4}},"selectionRange":{"start":{"line":1,"character":13},"end":{"line":1,"character":18}},"children":[{"name":"Tick","kind":6,"range":{"start":{"line":3,"character":4},"end":{"line":4,"character":5}},"selectionRange":{"start":{"line":3,"character":16},"end":{"line":3,"character":20}}}]}]}]
            """);

        var symbols = LspSemanticResponseNormalizer.ExtractDocumentSymbols("src/Clock.cs", response.RootElement, CSharpIdentity);

        Assert.True(symbols.IsSuccess);
        var tick = Assert.Single(symbols.Value, static symbol => symbol.DisplayName == "Tick");
        Assert.Equal(SemanticSymbolKind.Method, tick.Kind);
        Assert.Equal("csharp:Acme.Clock.Tick@src/Clock.cs:4:17", tick.CanonicalId);
        Assert.Equal("csharp:Acme.Clock@src/Clock.cs:2:14", tick.ContainerCanonicalId);
        Assert.Equal(4, tick.Range.StartLine);
        Assert.Equal(17, tick.Range.StartColumn);
        Assert.Equal(new SemanticSourceRange("src/Clock.cs", 4, 5, 5, 6), tick.ScopeRange);
    }

    [Fact]
    public void DefinitionLocationResolvesOnlyToKnownSemanticTarget()
    {
        using var symbolsResponse = JsonDocument.Parse("""
            [{"name":"Clock","kind":5,"selectionRange":{"start":{"line":1,"character":13},"end":{"line":4,"character":1}},"children":[{"name":"Tick","kind":6,"selectionRange":{"start":{"line":3,"character":16},"end":{"line":3,"character":20}}}]}]
            """);
        var symbols = LspSemanticResponseNormalizer.ExtractDocumentSymbols("src/Clock.cs", symbolsResponse.RootElement, CSharpIdentity);
        using var definitionResponse = JsonDocument.Parse("""
            [{"targetUri":"file:///repo/src/Clock.cs","targetSelectionRange":{"start":{"line":3,"character":17},"end":{"line":3,"character":20}}}]
            """);

        var definitions = LspSemanticResponseNormalizer.ExtractDefinitions("csharp:Caller@src/Program.cs:1:1", "/repo", definitionResponse.RootElement, symbols.Value);

        Assert.True(definitions.IsSuccess);
        var definition = Assert.Single(definitions.Value);
        Assert.Equal("csharp:Clock.Tick@src/Clock.cs:4:17", definition.TargetCanonicalId);
        Assert.Equal("src/Clock.cs", definition.Range.RepositoryRelativePath);
    }

    [Fact]
    public void DelegatesUnknownLspKindsToTheSelectedAdapterIdentityPolicy()
    {
        using var response = JsonDocument.Parse("""
            [{"name":"LanguageSpecificThing","kind":19,"selectionRange":{"start":{"line":0,"character":0},"end":{"line":0,"character":21}}}]
            """);

        var symbols = LspSemanticResponseNormalizer.ExtractDocumentSymbols("src/Thing.cs", response.RootElement, new TestIdentityPolicy());

        Assert.True(symbols.IsSuccess);
        var symbol = Assert.Single(symbols.Value);
        Assert.Equal("test:LanguageSpecificThing", symbol.CanonicalId);
        Assert.Equal(SemanticSymbolKind.Event, symbol.Kind);
    }

    private sealed class TestIdentityPolicy : ILspSemanticSymbolIdentityPolicy
    {
        public string AdapterId => "test";

        public SemanticSymbol CreateSymbol(string repositoryRelativePath, string qualifiedName, string displayName, int lspSymbolKind, string? containerCanonicalId, SemanticSourceRange range) =>
            new($"test:{qualifiedName}", displayName, SemanticSymbolKind.Event, containerCanonicalId, "unknown", range);
    }

    private static LanguageServerProfileConfiguration CSharpProfile() => new(
        "csharp",
        "csharp",
        [".cs"],
        ["*.csproj"],
        "fixture-lsp",
        [],
        "csharp",
        [
            new LanguageServerSymbolKindMapping("namespace", [3]),
            new LanguageServerSymbolKindMapping("type", [5]),
            new LanguageServerSymbolKindMapping("method", [6]),
        ]);
}
