using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Analysis.MapSemanticGraphFacts;

namespace Archy.UnitTests.Features.Analysis.MapSemanticGraphFacts;

public sealed class SemanticGraphFactMapperTests
{
    [Fact]
    public void MapsSymbolsContainmentAndResolvedDefinitionsUsingCallerSuppliedProvider()
    {
        var type = Symbol("csharp:Acme.Clock@src/Clock.cs:1:14", "Clock", SemanticSymbolKind.Type, null, 1, 14, 6, 2);
        var method = Symbol("csharp:Acme.Clock.Tick@src/Clock.cs:3:17", "Tick", SemanticSymbolKind.Method, type.CanonicalId, 3, 17, 3, 21);
        var facts = SemanticGraphFactMapper.Map(
            "roslyn-semantic",
            [type, method],
            [new SemanticDefinition(method.CanonicalId, type.CanonicalId, new SemanticSourceRange("src/Clock.cs", 3, 17, 3, 21))],
            [],
            [],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/Clock.cs"] = "ABC" });

        Assert.True(facts.IsSuccess);
        Assert.Equal(2, facts.Value.Nodes.Count);
        Assert.Equal(2, facts.Value.Symbols.Count);
        Assert.Contains(facts.Value.Edges, static edge => edge.EdgeKind == "contains");
        Assert.Contains(facts.Value.Edges, static edge => edge.EdgeKind == "defines");
        Assert.All(facts.Value.Nodes, static node => Assert.Equal("roslyn-semantic", node.Provider));
    }

    [Fact]
    public void OmitsDefinitionsWhoseSourceOrTargetIsNotInTheSemanticBatch()
    {
        var type = Symbol("csharp:Clock@src/Clock.cs:1:14", "Clock", SemanticSymbolKind.Type, null, 1, 14, 2, 2);
        var facts = SemanticGraphFactMapper.Map(
            "test-semantic",
            [type],
            [new SemanticDefinition("csharp:unknown", type.CanonicalId, type.Range)],
            [],
            [],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/Clock.cs"] = "ABC" });

        Assert.True(facts.IsSuccess);
        Assert.DoesNotContain(facts.Value.Edges, static edge => edge.EdgeKind == "defines");
    }

    [Fact]
    public void MapsAllResolvedStandardLspRelationshipKinds()
    {
        var type = Symbol("ts:Clock@src/Clock.ts:1:14", "Clock", SemanticSymbolKind.Type, null, 1, 14, 8, 2, "src/Clock.ts");
        var caller = Symbol("ts:Clock.Run@src/Clock.ts:3:5", "Run", SemanticSymbolKind.Method, type.CanonicalId, 3, 5, 5, 2, "src/Clock.ts");
        var callee = Symbol("ts:Clock.Tick@src/Clock.ts:6:5", "Tick", SemanticSymbolKind.Method, type.CanonicalId, 6, 5, 7, 2, "src/Clock.ts");
        var facts = SemanticGraphFactMapper.Map(
            "typescript-semantic",
            [type, caller, callee],
            [],
            [new SemanticReference(caller.CanonicalId, callee.CanonicalId, new SemanticSourceRange("src/Clock.ts", 4, 9, 4, 13))],
            [new SemanticCall(caller.CanonicalId, callee.CanonicalId, new SemanticSourceRange("src/Clock.ts", 4, 9, 4, 13))],
            [new SemanticInheritance(type.CanonicalId, callee.CanonicalId, new SemanticSourceRange("src/Clock.ts", 1, 14, 1, 19))],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/Clock.ts"] = "DEF" });

        Assert.True(facts.IsSuccess);
        Assert.Contains(facts.Value.Edges, static edge => edge.EdgeKind == "references");
        Assert.Contains(facts.Value.Edges, static edge => edge.EdgeKind == "calls");
        Assert.Contains(facts.Value.Edges, static edge => edge.EdgeKind == "inherits");
    }

    private static SemanticSymbol Symbol(string id, string name, SemanticSymbolKind kind, string? container, int startLine, int startColumn, int endLine, int endColumn, string path = "src/Clock.cs") => new(id, name, kind, container, "public", new SemanticSourceRange(path, startLine, startColumn, endLine, endColumn));
}
