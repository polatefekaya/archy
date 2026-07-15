using System.Text.Json;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.CommitGraphRevision;

internal static class GraphFactValidator
{
    public static Problem? Validate(
        IReadOnlyList<GraphNodeFact> nodes,
        IReadOnlyList<GraphEdgeFact> edges,
        IReadOnlyList<GraphSymbolFact> symbols,
        IReadOnlyList<InterfaceFingerprintFact> interfaceFingerprints)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node is null ||
                string.IsNullOrWhiteSpace(node.StableId) ||
                string.IsNullOrWhiteSpace(node.NodeKind) ||
                string.IsNullOrWhiteSpace(node.CanonicalKey) ||
                string.IsNullOrWhiteSpace(node.Provider) ||
                !IsJson(node.EvidenceJson) ||
                string.IsNullOrWhiteSpace(node.ContentHash) ||
                !IsProbability(node.Confidence) ||
                !HasValidSourceRange(node) ||
                !nodeIds.Add(node.StableId))
            {
                return Problem.Validation(
                    "Graph nodes require unique stable IDs, content hashes, evidence, providers, and confidence between zero and one.");
            }
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (edge is null ||
                string.IsNullOrWhiteSpace(edge.EdgeId) ||
                !edgeIds.Add(edge.EdgeId) ||
                !nodeIds.Contains(edge.SourceStableId) ||
                !nodeIds.Contains(edge.TargetStableId) ||
                string.IsNullOrWhiteSpace(edge.EdgeKind) ||
                string.IsNullOrWhiteSpace(edge.Provider) ||
                !IsJson(edge.EvidenceJson) ||
                !IsProbability(edge.Confidence))
            {
                return Problem.Validation(
                    "Graph edges require unique IDs, in-revision endpoints, evidence, providers, and confidence between zero and one.");
            }
        }

        var symbolIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol is null ||
                string.IsNullOrWhiteSpace(symbol.SymbolId) ||
                !symbolIds.Add(symbol.SymbolId) ||
                !nodeIds.Contains(symbol.NodeStableId) ||
                string.IsNullOrWhiteSpace(symbol.FullyQualifiedName) ||
                string.IsNullOrWhiteSpace(symbol.Visibility) ||
                string.IsNullOrWhiteSpace(symbol.NormalizedSignature) ||
                !IsJson(symbol.ParameterMetadataJson) ||
                !IsJson(symbol.ReturnMetadataJson) ||
                string.IsNullOrWhiteSpace(symbol.SignatureHash))
            {
                return Problem.Validation(
                    "Symbols require unique IDs, in-revision nodes, signatures, visibility, metadata, and signature hashes.");
            }
        }

        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interfaceFingerprint in interfaceFingerprints)
        {
            if (interfaceFingerprint is null)
            {
                return Problem.Validation(
                    "Interface fingerprints require unique symbol/kind pairs, in-revision symbols, hashes, and normalized members.");
            }

            var fingerprintIdentity = string.Concat(
                interfaceFingerprint.SymbolId,
                "\u001f",
                interfaceFingerprint.FingerprintKind);
            if (!symbolIds.Contains(interfaceFingerprint.SymbolId) ||
                string.IsNullOrWhiteSpace(interfaceFingerprint.FingerprintKind) ||
                string.IsNullOrWhiteSpace(interfaceFingerprint.FingerprintHash) ||
                !IsJson(interfaceFingerprint.NormalizedMembersJson) ||
                !fingerprints.Add(fingerprintIdentity))
            {
                return Problem.Validation(
                    "Interface fingerprints require unique symbol/kind pairs, in-revision symbols, hashes, and normalized members.");
            }
        }

        return null;
    }

    private static bool HasValidSourceRange(GraphNodeFact node) =>
        node.StartLine.HasValue == node.EndLine.HasValue &&
        node.StartLine is not <= 0 &&
        node.EndLine is not <= 0 &&
        (node.StartLine is null || node.StartLine <= node.EndLine);

    private static bool IsProbability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool IsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
