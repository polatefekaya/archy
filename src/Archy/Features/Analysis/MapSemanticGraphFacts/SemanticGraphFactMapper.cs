using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.MapSemanticGraphFacts;

public sealed record SemanticGraphFacts(
    IReadOnlyList<GraphNodeFact> Nodes,
    IReadOnlyList<GraphEdgeFact> Edges,
    IReadOnlyList<GraphSymbolFact> Symbols);

/// <summary>Converts adapter-neutral semantic facts into a graph revision batch.</summary>
public static class SemanticGraphFactMapper
{
    public static Result<SemanticGraphFacts> Map(
        string provider,
        IReadOnlyList<SemanticSymbol> semanticSymbols,
        IReadOnlyList<SemanticDefinition> definitions,
        IReadOnlyList<SemanticReference> references,
        IReadOnlyList<SemanticCall> calls,
        IReadOnlyList<SemanticInheritance> inheritance,
        IReadOnlyDictionary<string, string> contentHashes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(semanticSymbols);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(inheritance);
        ArgumentNullException.ThrowIfNull(contentHashes);
        if (semanticSymbols.Any(static symbol => symbol is null) ||
            definitions.Any(static definition => definition is null) ||
            references.Any(static reference => reference is null) ||
            calls.Any(static call => call is null) ||
            inheritance.Any(static edge => edge is null) ||
            contentHashes.Any(static entry => !IsRelative(entry.Key) || string.IsNullOrWhiteSpace(entry.Value)))
        {
            return ResultFactory.Failure<SemanticGraphFacts>(Problem.Validation("Semantic graph conversion requires complete semantic facts and repository-relative source hashes."));
        }

        if (semanticSymbols.Select(static symbol => symbol.CanonicalId).Distinct(StringComparer.Ordinal).Count() != semanticSymbols.Count || semanticSymbols.Any(symbol => !contentHashes.ContainsKey(symbol.Range.RepositoryRelativePath)))
        {
            return ResultFactory.Failure<SemanticGraphFacts>(Problem.Validation("Semantic graph conversion requires unique symbols whose source hashes are present in the immutable snapshot."));
        }

        var byCanonicalId = semanticSymbols.ToDictionary(static symbol => symbol.CanonicalId, StringComparer.Ordinal);
        var nodes = semanticSymbols.Select(symbol => Node(symbol, provider, contentHashes[symbol.Range.RepositoryRelativePath])).OrderBy(static node => node.StableId, StringComparer.Ordinal).ToArray();
        var symbols = semanticSymbols.Select(Symbol).OrderBy(static symbol => symbol.SymbolId, StringComparer.Ordinal).ToArray();
        var edges = new List<GraphEdgeFact>();
        foreach (var symbol in semanticSymbols.Where(static symbol => symbol.ContainerCanonicalId is not null))
        {
            if (byCanonicalId.ContainsKey(symbol.ContainerCanonicalId!))
            {
                edges.Add(Edge(symbol.ContainerCanonicalId!, symbol.CanonicalId, "contains", provider, symbol.Range));
            }
        }

        foreach (var definition in definitions)
        {
            if (byCanonicalId.ContainsKey(definition.SourceCanonicalId) && byCanonicalId.ContainsKey(definition.TargetCanonicalId))
            {
                edges.Add(Edge(definition.SourceCanonicalId, definition.TargetCanonicalId, "defines", provider, definition.Range));
            }
        }

        foreach (var reference in references)
        {
            if (byCanonicalId.ContainsKey(reference.SourceCanonicalId) && byCanonicalId.ContainsKey(reference.TargetCanonicalId))
            {
                edges.Add(Edge(reference.SourceCanonicalId, reference.TargetCanonicalId, "references", provider, reference.Range));
            }
        }

        foreach (var call in calls)
        {
            if (byCanonicalId.ContainsKey(call.CallerCanonicalId) && byCanonicalId.ContainsKey(call.CalleeCanonicalId))
            {
                edges.Add(Edge(call.CallerCanonicalId, call.CalleeCanonicalId, "calls", provider, call.Range));
            }
        }

        foreach (var inheritanceEdge in inheritance)
        {
            if (byCanonicalId.ContainsKey(inheritanceEdge.DerivedCanonicalId) && byCanonicalId.ContainsKey(inheritanceEdge.BaseCanonicalId))
            {
                edges.Add(Edge(inheritanceEdge.DerivedCanonicalId, inheritanceEdge.BaseCanonicalId, "inherits", provider, inheritanceEdge.Range));
            }
        }

        return ResultFactory.Success(new SemanticGraphFacts(nodes, [.. edges.DistinctBy(static edge => edge.EdgeId).OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)], symbols));
    }

    private static GraphNodeFact Node(SemanticSymbol symbol, string provider, string contentHash) => new(NodeId(symbol.CanonicalId), $"semantic_{symbol.Kind.ToString().ToLowerInvariant()}", symbol.CanonicalId, symbol.DisplayName, symbol.Range.RepositoryRelativePath, symbol.Range.StartLine, symbol.Range.EndLine, provider, 1, Evidence("symbol", symbol.Range), contentHash);

    private static GraphSymbolFact Symbol(SemanticSymbol symbol)
    {
        var signature = $"{symbol.Kind}:{symbol.CanonicalId}";
        return new GraphSymbolFact($"semantic-symbol:{symbol.CanonicalId}", NodeId(symbol.CanonicalId), symbol.CanonicalId, symbol.Visibility, signature, "[]", "{}", Hash(signature));
    }

    private static GraphEdgeFact Edge(string source, string target, string kind, string provider, SemanticSourceRange range) => new($"semantic-edge:{Hash($"{source}|{target}|{kind}|{range.RepositoryRelativePath}|{range.StartLine}|{range.StartColumn}")}", NodeId(source), NodeId(target), kind, null, provider, 1, Evidence(kind, range));

    private static string NodeId(string canonicalId) => $"semantic-node:{canonicalId}";

    private static string Evidence(string kind, SemanticSourceRange range) => JsonSerializer.Serialize(new SemanticEvidence(kind, range.RepositoryRelativePath, range.StartLine, range.StartColumn, range.EndLine, range.EndColumn), SemanticGraphFactJsonContext.Default.SemanticEvidence);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsRelative(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static part => part == "..");

    internal sealed record SemanticEvidence(string Kind, string Path, int StartLine, int StartColumn, int EndLine, int EndColumn);
}
