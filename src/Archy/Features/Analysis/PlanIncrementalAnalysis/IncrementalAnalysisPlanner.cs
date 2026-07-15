using Archy.Features.Analysis.InventorySources;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Analysis.PlanIncrementalAnalysis;

public sealed class IncrementalAnalysisPlanner : IIncrementalAnalysisPlanner
{
    private const string CSharpSyntaxProvider = "csharp-syntax";

    public IncrementalAnalysisPlan Create(SourceInventory inventory, GraphRevisionSnapshot? activeRevision)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var changed = inventory.Changes
            .Where(static change => change.Kind is not SourceFileChangeKind.Unchanged)
            .OrderBy(static change => change.File.RepositoryRelativePath, StringComparer.Ordinal)
            .ThenBy(static change => change.Kind)
            .ToArray();
        var changedPaths = changed
            .Select(static change => change.File.RepositoryRelativePath)
            .Concat(changed
                .Where(static change => change.Kind == SourceFileChangeKind.Moved)
                .Select(static change => change.PreviousFile?.RepositoryRelativePath)
                .OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var deletedPaths = changed
            .Where(static change => change.Kind is SourceFileChangeKind.Deleted or SourceFileChangeKind.Moved)
            .Select(static change => change.Kind == SourceFileChangeKind.Deleted
                ? change.File.RepositoryRelativePath
                : change.PreviousFile?.RepositoryRelativePath)
            .OfType<string>()
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var changedCSharpPaths = changed
            .Where(static change => change.File.Language == SourceLanguage.CSharp)
            .Select(static change => change.File.RepositoryRelativePath)
            .ToHashSet(StringComparer.Ordinal);
        var csharpFilesToParse = inventory.Files
            .Where(file => file.Language == SourceLanguage.CSharp && changedCSharpPaths.Contains(file.RepositoryRelativePath))
            .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal)
            .ToArray();

        if (changed.Length == 0)
        {
            return new IncrementalAnalysisPlan(
                IncrementalAnalysisPlanReason.NoSourceChanges,
                ReusesCSharpSyntaxFacts: false,
                RequiresFullSemanticSnapshot: false,
                ChangedPaths: [],
                DeletedPaths: [],
                CSharpFilesToParse: [],
                AffectedStableIds: [],
                DirectDependentPaths: [],
                AffectedProviderIds: [],
                AffectedJoinKeys: []);
        }

        if (activeRevision is null)
        {
            return FullSyntaxPlan(
                IncrementalAnalysisPlanReason.InitialScan,
                inventory,
                changedPaths,
                deletedPaths);
        }

        if (!HasReusableCSharpCoverage(inventory, activeRevision, changedCSharpPaths))
        {
            return FullSyntaxPlan(
                IncrementalAnalysisPlanReason.SnapshotCoverageUnavailable,
                inventory,
                changedPaths,
                deletedPaths);
        }

        var changedPathSet = changedPaths.ToHashSet(StringComparer.Ordinal);
        var affectedNodeIds = activeRevision.Nodes
            .Where(node => node.FilePath is not null && changedPathSet.Contains(node.FilePath))
            .Select(static node => node.StableId)
            .ToHashSet(StringComparer.Ordinal);
        var touchedEdges = activeRevision.Edges
            .Where(edge => affectedNodeIds.Contains(edge.SourceStableId) || affectedNodeIds.Contains(edge.TargetStableId))
            .OrderBy(static edge => edge.EdgeId, StringComparer.Ordinal)
            .ToArray();
        foreach (var edge in touchedEdges)
        {
            affectedNodeIds.Add(edge.SourceStableId);
            affectedNodeIds.Add(edge.TargetStableId);
        }

        var directDependentPaths = activeRevision.Nodes
            .Where(node => affectedNodeIds.Contains(node.StableId) && node.FilePath is not null && !changedPathSet.Contains(node.FilePath))
            .Select(static node => node.FilePath!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        return new IncrementalAnalysisPlan(
            IncrementalAnalysisPlanReason.IncrementalReuse,
            ReusesCSharpSyntaxFacts: true,
            RequiresFullSemanticSnapshot: true,
            ChangedPaths: changedPaths,
            DeletedPaths: deletedPaths,
            CSharpFilesToParse: csharpFilesToParse,
            AffectedStableIds: [.. affectedNodeIds.OrderBy(static id => id, StringComparer.Ordinal)],
            DirectDependentPaths: directDependentPaths,
            AffectedProviderIds: [.. touchedEdges
                .Select(static edge => edge.Provider)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static provider => provider, StringComparer.Ordinal)],
            AffectedJoinKeys: [.. touchedEdges
                .Select(static edge => edge.NormalizedJoinKey)
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static key => key, StringComparer.Ordinal)]);
    }

    private static IncrementalAnalysisPlan FullSyntaxPlan(
        IncrementalAnalysisPlanReason reason,
        SourceInventory inventory,
        IReadOnlyList<string> changedPaths,
        IReadOnlyList<string> deletedPaths) =>
        new(
            reason,
            ReusesCSharpSyntaxFacts: false,
            RequiresFullSemanticSnapshot: true,
            ChangedPaths: changedPaths,
            DeletedPaths: deletedPaths,
            CSharpFilesToParse: [.. inventory.Files
                .Where(static file => file.Language == SourceLanguage.CSharp)
                .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal)],
            AffectedStableIds: [],
            DirectDependentPaths: [],
            AffectedProviderIds: [],
            AffectedJoinKeys: []);

    private static bool HasReusableCSharpCoverage(
        SourceInventory inventory,
        GraphRevisionSnapshot activeRevision,
        HashSet<string> changedCSharpPaths)
    {
        var coveredPaths = activeRevision.Nodes
            .Where(static node => node.Provider == CSharpSyntaxProvider && node.NodeKind == "file" && node.FilePath is not null)
            .Select(static node => node.FilePath!)
            .ToHashSet(StringComparer.Ordinal);
        return inventory.Files
            .Where(file => file.Language == SourceLanguage.CSharp && !changedCSharpPaths.Contains(file.RepositoryRelativePath))
            .All(file => coveredPaths.Contains(file.RepositoryRelativePath));
    }
}
