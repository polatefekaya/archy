using System.Security.Cryptography;
using System.Text;
using Archy.Features.Duplicates.ParseJscpdCloneReport;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Duplicates.MapStructuralClonesToSymbols;

/// <summary>Turns untrusted sidecar ranges into graph evidence only when their executable-symbol ownership is unambiguous.</summary>
public sealed class StructuralCloneSymbolMapper : IStructuralCloneSymbolMapper
{
    public StructuralCloneSymbolMapping Map(GraphRevisionSnapshot graph, IReadOnlyList<StructuralCloneOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(occurrences);

        var executableNodes = graph.Nodes
            .Where(IsExecutableNode)
            .Where(static node => node.FilePath is not null && node.StartLine is not null && node.EndLine is not null)
            .Where(static node => !IsGeneratedPath(node.FilePath!))
            .OrderBy(static node => node.FilePath, StringComparer.Ordinal)
            .ThenBy(static node => node.StartLine)
            .ThenBy(static node => node.EndLine)
            .ThenBy(static node => node.StableId, StringComparer.Ordinal)
            .ToArray();

        var pairEvidence = new SortedDictionary<(string Left, string Right), List<StructuralCloneSymbolEvidence>>();
        var excluded = new List<StructuralCloneMappingExclusion>();

        foreach (var occurrence in occurrences.OrderBy(OccurrenceKey, StringComparer.Ordinal))
        {
            var first = Resolve(executableNodes, occurrence.First);
            var second = Resolve(executableNodes, occurrence.Second);
            if (first.Kind != ResolutionKind.Mapped || second.Kind != ResolutionKind.Mapped)
            {
                excluded.Add(new StructuralCloneMappingExclusion(occurrence, ResolutionReason(first, second)));
                continue;
            }

            if (string.Equals(first.Node!.StableId, second.Node!.StableId, StringComparison.Ordinal))
            {
                excluded.Add(new StructuralCloneMappingExclusion(occurrence, "same-executable-symbol"));
                continue;
            }

            var reverse = string.CompareOrdinal(first.Node.StableId, second.Node.StableId) > 0;
            var leftNode = reverse ? second.Node : first.Node;
            var rightNode = reverse ? first.Node : second.Node;
            var evidence = reverse
                ? new StructuralCloneSymbolEvidence(occurrence.Second, occurrence.First, occurrence.TokenCount, occurrence.LineCount, occurrence.Format)
                : new StructuralCloneSymbolEvidence(occurrence.First, occurrence.Second, occurrence.TokenCount, occurrence.LineCount, occurrence.Format);
            var pair = (leftNode.StableId, rightNode.StableId);
            if (!pairEvidence.TryGetValue(pair, out var evidenceForPair))
            {
                evidenceForPair = [];
                pairEvidence.Add(pair, evidenceForPair);
            }

            evidenceForPair.Add(evidence);
        }

        var pairs = pairEvidence
            .Select(static entry => new StructuralCloneSymbolPair(
                PairId(entry.Key.Left, entry.Key.Right),
                entry.Key.Left,
                entry.Key.Right,
                [.. entry.Value.OrderBy(EvidenceKey, StringComparer.Ordinal)]))
            .ToArray();
        return new StructuralCloneSymbolMapping(pairs, [.. excluded.OrderBy(static exclusion => OccurrenceKey(exclusion.Occurrence), StringComparer.Ordinal)]);
    }

    private static Resolution Resolve(IReadOnlyList<GraphNodeFact> nodes, CloneSourceRange range)
    {
        if (IsGeneratedPath(range.RepositoryRelativePath))
        {
            return new Resolution(ResolutionKind.Generated, null);
        }

        var candidates = nodes
            .Where(node => string.Equals(NormalizePath(node.FilePath!), NormalizePath(range.RepositoryRelativePath), StringComparison.Ordinal))
            .Where(node => node.StartLine <= range.StartLine && node.EndLine >= range.EndLine)
            .OrderBy(static node => node.EndLine!.Value - node.StartLine!.Value)
            .ThenBy(static node => node.StableId, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new Resolution(ResolutionKind.Unmapped, null);
        }

        var narrowestSpan = candidates[0].EndLine!.Value - candidates[0].StartLine!.Value;
        if (candidates.Count(node => node.EndLine!.Value - node.StartLine!.Value == narrowestSpan) != 1)
        {
            return new Resolution(ResolutionKind.Ambiguous, null);
        }

        return new Resolution(ResolutionKind.Mapped, candidates[0]);
    }

    private static bool IsExecutableNode(GraphNodeFact node) =>
        string.Equals(node.NodeKind, "method", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.NodeKind, "function", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.NodeKind, "semantic_method", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(node.NodeKind, "semantic_function", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedPath(string path)
    {
        var normalized = NormalizePath(path);
        var fileName = Path.GetFileName(normalized);
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                   .Any(static segment => string.Equals(segment, "generated", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)) ||
               fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolutionReason(Resolution first, Resolution second)
    {
        if (first.Kind == ResolutionKind.Generated || second.Kind == ResolutionKind.Generated) return "generated-source";
        if (first.Kind == ResolutionKind.Ambiguous || second.Kind == ResolutionKind.Ambiguous) return "ambiguous-executable-symbol";
        return "no-containing-executable-symbol";
    }

    private static string PairId(string leftStableId, string rightStableId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"structural-clone-v1\\n{leftStableId}\\n{rightStableId}"))).ToLowerInvariant();

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string OccurrenceKey(StructuralCloneOccurrence occurrence) => $"{EvidenceKey(new StructuralCloneSymbolEvidence(occurrence.First, occurrence.Second, occurrence.TokenCount, occurrence.LineCount, occurrence.Format))}";
    private static string EvidenceKey(StructuralCloneSymbolEvidence evidence) => $"{evidence.LeftRange.RepositoryRelativePath}\\u001f{evidence.LeftRange.StartLine:D8}\\u001f{evidence.RightRange.RepositoryRelativePath}\\u001f{evidence.RightRange.StartLine:D8}\\u001f{evidence.TokenCount:D8}\\u001f{evidence.LineCount:D8}\\u001f{evidence.Format}";

    private sealed record Resolution(ResolutionKind Kind, GraphNodeFact? Node);
    private enum ResolutionKind { Mapped, Unmapped, Ambiguous, Generated }
}
