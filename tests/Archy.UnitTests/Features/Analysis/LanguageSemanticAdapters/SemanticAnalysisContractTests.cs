using System.Text.Json;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Analysis.LanguageSemanticAdapters;

public sealed class SemanticAnalysisContractTests
{
    [Fact]
    public async Task FakeAdapterRoundTripsTheCompleteSemanticSurface()
    {
        var request = new SemanticAnalysisRequest(SemanticAnalysisContract.CurrentSchemaVersion, "/repo", "snapshot-1", [new SemanticDocument("src/Clock.cs", "ABC", "csharp")]);
        var adapter = new FakeAdapter();
        var result = await adapter.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(SemanticAnalysisContract.ValidateRequest(request));
        Assert.Null(SemanticAnalysisContract.ValidateResult(result.Value));
        var json = JsonSerializer.Serialize(result.Value, SemanticAnalysisJsonContext.Default.SemanticAnalysisResult);
        var restored = JsonSerializer.Deserialize(json, SemanticAnalysisJsonContext.Default.SemanticAnalysisResult);
        Assert.Equal("snapshot-1", restored!.SnapshotId);
        Assert.Single(restored.Symbols);
        Assert.Equal(new SemanticSourceRange("src/Clock.cs", 1, 1, 1, 10), restored.Symbols[0].ScopeRange);
        Assert.Single(restored.References);
        Assert.Single(restored.SyntaxSites);
    }

    [Fact]
    public void ContractRejectsAbsoluteRangesAndMismatchedSchema()
    {
        var request = new SemanticAnalysisRequest("old", "/repo", "snapshot", [new SemanticDocument("/absolute.cs", "ABC", "csharp")]);
        Assert.NotNull(SemanticAnalysisContract.ValidateRequest(request));
    }

    [Fact]
    public void ContractRejectsASymbolScopeFromAnotherDocument()
    {
        var result = new SemanticAnalysisResult(
            SemanticAnalysisContract.CurrentSchemaVersion,
            "fixture",
            "typescript",
            "snapshot",
            [],
            [new SemanticSymbol("ts:Thing", "Thing", SemanticSymbolKind.Type, null, "public", new SemanticSourceRange("src/thing.ts", 1, 1, 1, 6), new SemanticSourceRange("src/other.ts", 1, 1, 2, 1))],
            [], [], [], [], [], [], []);

        Assert.NotNull(SemanticAnalysisContract.ValidateResult(result));
    }

    private sealed class FakeAdapter : ILanguageSemanticAdapter
    {
        public string AdapterId => "fixture";
        public string Language => "csharp";
        public ValueTask<Result<SemanticAnalysisResult>> AnalyzeAsync(SemanticAnalysisRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(ResultFactory.Success(new SemanticAnalysisResult(
            SemanticAnalysisContract.CurrentSchemaVersion, AdapterId, Language, request.SnapshotId,
            [new SemanticCapability("references", SemanticCapabilityState.Available, null)],
            [new SemanticSymbol("csharp:Sample.Clock", "Clock", SemanticSymbolKind.Type, null, "public", Range(), Range())],
            [], [new SemanticReference("csharp:Sample.Clock", "csharp:System.IDisposable", Range())], [], [], [],
            [new SemanticSyntaxSite("invocation", [new SemanticSyntaxCapture("method", "Dispose", "identifier")], Range())], [])));
        private static SemanticSourceRange Range() => new("src/Clock.cs", 1, 1, 1, 10);
    }
}
