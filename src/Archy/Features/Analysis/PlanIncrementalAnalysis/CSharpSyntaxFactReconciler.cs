using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.PlanIncrementalAnalysis;

/// <summary>Rebuilds the complete C# syntax input from one active immutable revision and changed files only.</summary>
public sealed class CSharpSyntaxFactReconciler : ICSharpSyntaxFactReconciler
{
    private const string CSharpSyntaxProvider = "csharp-syntax";

    public Result<CSharpSyntaxFactBatch> Reconcile(
        GraphRevisionSnapshot activeRevision,
        IncrementalAnalysisPlan plan,
        CSharpSyntaxFactBatch changedFacts)
    {
        ArgumentNullException.ThrowIfNull(activeRevision);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(changedFacts);
        if (!plan.ReusesCSharpSyntaxFacts)
        {
            return ResultFactory.Failure<CSharpSyntaxFactBatch>(
                Problem.Validation("C# syntax facts can only be reconciled from an incremental reuse plan."));
        }

        var changedPaths = plan.ChangedPaths.ToHashSet(StringComparer.Ordinal);
        var existingSyntaxNodes = activeRevision.Nodes
            .Where(static node => node.Provider == CSharpSyntaxProvider)
            .ToArray();
        var replacedNodeIds = existingSyntaxNodes
            .Where(node => node.FilePath is not null && changedPaths.Contains(node.FilePath))
            .Select(static node => node.StableId)
            .ToHashSet(StringComparer.Ordinal);
        var preservedNodes = existingSyntaxNodes
            .Where(node => node.FilePath is null || !changedPaths.Contains(node.FilePath))
            .ToArray();
        var preservedEdges = activeRevision.Edges
            .Where(static edge => edge.Provider == CSharpSyntaxProvider)
            .Where(edge => !replacedNodeIds.Contains(edge.SourceStableId) && !replacedNodeIds.Contains(edge.TargetStableId))
            .ToArray();

        var nodes = MergeByIdentity(
            preservedNodes,
            changedFacts.Nodes,
            static node => node.StableId,
            "node");
        if (!nodes.IsSuccess)
        {
            return ResultFactory.Failure<CSharpSyntaxFactBatch>(nodes.Problem!);
        }

        var edges = MergeByIdentity(
            preservedEdges,
            changedFacts.Edges,
            static edge => edge.EdgeId,
            "edge");
        if (!edges.IsSuccess)
        {
            return ResultFactory.Failure<CSharpSyntaxFactBatch>(edges.Problem!);
        }

        var referencedNodeIds = edges.Value
            .SelectMany(static edge => new[] { edge.SourceStableId, edge.TargetStableId })
            .ToHashSet(StringComparer.Ordinal);
        var retainedNodes = nodes.Value
            .Where(node => node.FilePath is not null || referencedNodeIds.Contains(node.StableId))
            .OrderBy(static node => node.StableId, StringComparer.Ordinal)
            .ToArray();
        var retainedNodeIds = retainedNodes.Select(static node => node.StableId).ToHashSet(StringComparer.Ordinal);

        var preservedSymbols = activeRevision.Symbols
            .Where(symbol => retainedNodeIds.Contains(symbol.NodeStableId) && !replacedNodeIds.Contains(symbol.NodeStableId))
            .ToArray();
        var symbols = MergeByIdentity(
            preservedSymbols,
            changedFacts.Symbols,
            static symbol => symbol.SymbolId,
            "symbol");
        if (!symbols.IsSuccess)
        {
            return ResultFactory.Failure<CSharpSyntaxFactBatch>(symbols.Problem!);
        }

        var retainedSymbolIds = symbols.Value.Select(static symbol => symbol.SymbolId).ToHashSet(StringComparer.Ordinal);
        var preservedFingerprints = activeRevision.InterfaceFingerprints
            .Where(fingerprint => retainedSymbolIds.Contains(fingerprint.SymbolId) && !changedFacts.Symbols.Any(symbol => symbol.SymbolId == fingerprint.SymbolId))
            .ToArray();
        var fingerprints = MergeByIdentity(
            preservedFingerprints,
            changedFacts.InterfaceFingerprints,
            static fingerprint => $"{fingerprint.SymbolId}|{fingerprint.FingerprintKind}",
            "interface fingerprint");
        if (!fingerprints.IsSuccess)
        {
            return ResultFactory.Failure<CSharpSyntaxFactBatch>(fingerprints.Problem!);
        }

        return ResultFactory.Success(new CSharpSyntaxFactBatch(
            changedFacts.IsComplete,
            retainedNodes,
            [.. edges.Value.OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)],
            [.. symbols.Value.OrderBy(static symbol => symbol.SymbolId, StringComparer.Ordinal)],
            [.. fingerprints.Value
                .OrderBy(static fingerprint => fingerprint.SymbolId, StringComparer.Ordinal)
                .ThenBy(static fingerprint => fingerprint.FingerprintKind, StringComparer.Ordinal)],
            changedFacts.Diagnostics));
    }

    private static Result<IReadOnlyList<T>> MergeByIdentity<T, TKey>(
        IEnumerable<T> preserved,
        IEnumerable<T> replacements,
        Func<T, TKey> identity,
        string factName)
        where TKey : notnull
    {
        var facts = new Dictionary<TKey, T>();
        foreach (var fact in preserved.Concat(replacements))
        {
            var key = identity(fact);
            if (facts.TryGetValue(key, out var existing) && !EqualityComparer<T>.Default.Equals(existing, fact))
            {
                return ResultFactory.Failure<IReadOnlyList<T>>(
                    Problem.Conflict($"Incremental C# syntax reconciliation produced conflicting {factName} identity '{key}'."));
            }

            facts.TryAdd(key, fact);
        }

        return ResultFactory.Success<IReadOnlyList<T>>([.. facts.Values]);
    }
}
