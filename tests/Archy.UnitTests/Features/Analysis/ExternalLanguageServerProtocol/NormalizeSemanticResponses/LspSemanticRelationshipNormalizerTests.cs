using System.Text.Json;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.NormalizeSemanticResponses;
using Archy.Features.Analysis.LanguageSemanticAdapters;

namespace Archy.UnitTests.Features.Analysis.ExternalLanguageServerProtocol.NormalizeSemanticResponses;

public sealed class LspSemanticRelationshipNormalizerTests
{
    [Fact]
    public void ExtractCallsMapsOnlyDefinitionsThatResolveToKnownSymbols()
    {
        var caller = Symbol("csharp:Caller@src/Program.cs:1:1", "src/Program.cs", 1, 1, 8, 1);
        var target = Symbol("csharp:Target@src/Clock.cs:3:5", "src/Clock.cs", 3, 5, 5, 1);
        using var response = JsonDocument.Parse("""
            [{"targetUri":"file:///repo/src/Clock.cs","targetSelectionRange":{"start":{"line":2,"character":4},"end":{"line":2,"character":10}}}]
            """);

        var calls = LspSemanticRelationshipNormalizer.ExtractCalls(caller.CanonicalId, "/repo", response.RootElement, [caller, target]);

        Assert.True(calls.IsSuccess);
        var call = Assert.Single(calls.Value);
        Assert.Equal(caller.CanonicalId, call.CallerCanonicalId);
        Assert.Equal(target.CanonicalId, call.CalleeCanonicalId);
    }

    [Fact]
    public void ExtractReferencesAttributesNestedLocationToTheSmallestContainingSymbol()
    {
        var outer = Symbol("csharp:Container@src/Program.cs:1:1", "src/Program.cs", 1, 1, 10, 1);
        var inner = Symbol("csharp:Container.Use@src/Program.cs:3:5", "src/Program.cs", 3, 5, 5, 1);
        const string target = "csharp:Clock.Tick@src/Clock.cs:2:5";
        using var response = JsonDocument.Parse("""
            [{"uri":"file:///repo/src/Program.cs","range":{"start":{"line":3,"character":8},"end":{"line":3,"character":12}}}]
            """);

        var references = LspSemanticRelationshipNormalizer.ExtractReferences(target, "/repo", response.RootElement, [outer, inner]);

        Assert.True(references.IsSuccess);
        var reference = Assert.Single(references.Value);
        Assert.Equal(inner.CanonicalId, reference.SourceCanonicalId);
        Assert.Equal(target, reference.TargetCanonicalId);
    }

    [Fact]
    public void RejectsDefinitionLocationsOutsideTheRepository()
    {
        var source = Symbol("csharp:Caller@src/Program.cs:1:1", "src/Program.cs", 1, 1, 8, 1);
        using var response = JsonDocument.Parse("""
            [{"uri":"file:///outside/Other.cs","range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}}]
            """);

        var calls = LspSemanticRelationshipNormalizer.ExtractCalls(source.CanonicalId, "/repo", response.RootElement, [source]);

        Assert.False(calls.IsSuccess);
        Assert.Equal("validation", calls.Problem!.Code);
    }

    [Fact]
    public void ExtractOutgoingCallsUsesCallHierarchyTargetAndCallerScope()
    {
        var caller = Symbol(
            "ts:Caller@src/clock.ts:1:17",
            "src/clock.ts",
            1,
            17,
            1,
            23,
            new SemanticSourceRange("src/clock.ts", 1, 1, 3, 2));
        var target = Symbol("ts:Target@src/clock.ts:5:17", "src/clock.ts", 5, 17, 5, 23);
        using var response = JsonDocument.Parse("""
            [{"to":{"uri":"file:///repo/src/clock.ts","selectionRange":{"start":{"line":4,"character":16},"end":{"line":4,"character":22}}},"fromRanges":[{"start":{"line":1,"character":2},"end":{"line":1,"character":8}}]}]
            """);

        var calls = LspSemanticRelationshipNormalizer.ExtractOutgoingCalls(caller.CanonicalId, "/repo", response.RootElement, [caller, target]);

        Assert.True(calls.IsSuccess);
        var call = Assert.Single(calls.Value);
        Assert.Equal(caller.CanonicalId, call.CallerCanonicalId);
        Assert.Equal(target.CanonicalId, call.CalleeCanonicalId);
        Assert.Equal(new SemanticSourceRange("src/clock.ts", 2, 3, 2, 9), call.Range);
    }

    private static SemanticSymbol Symbol(string canonicalId, string path, int startLine, int startColumn, int endLine, int endColumn, SemanticSourceRange? scopeRange = null) =>
        new(canonicalId, canonicalId, SemanticSymbolKind.Method, null, "unknown", new SemanticSourceRange(path, startLine, startColumn, endLine, endColumn), scopeRange);
}
