using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.Features.Memory.DetermineImportantNodes;

/// <summary>Defines the explainable, language-neutral boundary between a graph revision and AI memory work.</summary>
public sealed class ImportantNodeEligibilityPolicy : IImportantNodeEligibilityPolicy
{
    public ImportantNodeEligibility Determine(
        GraphRevisionSnapshot snapshot,
        MemorySelectionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(configuration);

        var targets = new Dictionary<(ImportantNodeTargetKind Kind, string StableId), TargetAccumulator>();
        var excluded = new List<ImportantNodeExclusion>();
        var nodesByStableId = snapshot.Nodes.ToDictionary(static node => node.StableId, StringComparer.Ordinal);
        var nodesByFile = snapshot.Nodes
            .Where(static node => !string.IsNullOrWhiteSpace(node.FilePath))
            .GroupBy(static node => node.FilePath!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var eligiblePaths = nodesByFile.Keys
            .Where(path => configuration.IncludeGeneratedNodes || !IsGeneratedPath(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (var path in nodesByFile.Keys.Where(IsGeneratedPath).OrderBy(static path => path, StringComparer.Ordinal))
        {
            if (!configuration.IncludeGeneratedNodes)
            {
                excluded.Add(new ImportantNodeExclusion($"file:{path}", path, "generated-source"));
            }
        }

        AddDirectories(eligiblePaths, targets);
        AddConfiguredModules(eligiblePaths, configuration.ImportantModulePaths, targets);
        AddNamespaces(snapshot.Nodes, targets, configuration.IncludeGeneratedNodes, excluded);
        AddPublicSymbols(snapshot.Symbols, nodesByStableId, targets, configuration.IncludeGeneratedNodes, excluded);

        return new ImportantNodeEligibility(
            snapshot.Revision,
            [.. targets.Values
                .Select(static accumulator => accumulator.ToTarget())
                .OrderBy(static target => target.Kind)
                .ThenBy(static target => target.StableId, StringComparer.Ordinal)],
            [.. excluded
                .DistinctBy(static exclusion => (exclusion.StableId, exclusion.Reason))
                .OrderBy(static exclusion => exclusion.StableId, StringComparer.Ordinal)
                .ThenBy(static exclusion => exclusion.Reason, StringComparer.Ordinal)]);
    }

    private static void AddDirectories(
        IReadOnlyList<string> eligiblePaths,
        IDictionary<(ImportantNodeTargetKind Kind, string StableId), TargetAccumulator> targets)
    {
        foreach (var path in eligiblePaths)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var segmentCount = 1; segmentCount < segments.Length; segmentCount++)
            {
                var directory = string.Join('/', segments.Take(segmentCount));
                Add(targets, ImportantNodeTargetKind.Directory, $"directory:{directory}", directory, path, ImportantNodeEligibilityReason.DirectoryContainsSource);
            }
        }
    }

    private static void AddConfiguredModules(
        IReadOnlyList<string> eligiblePaths,
        IReadOnlyList<string> configuredModules,
        IDictionary<(ImportantNodeTargetKind Kind, string StableId), TargetAccumulator> targets)
    {
        foreach (var module in configuredModules.OrderBy(static path => path, StringComparer.Ordinal))
        {
            var normalized = NormalizeModulePath(module);
            var modulePaths = eligiblePaths.Where(path => IsSameOrDescendant(path, normalized)).ToArray();
            if (modulePaths.Length == 0)
            {
                continue;
            }

            foreach (var path in modulePaths)
            {
                Add(targets, ImportantNodeTargetKind.Module, $"module:{normalized}", normalized, path, ImportantNodeEligibilityReason.ConfiguredModule);
            }
        }
    }

    private static void AddNamespaces(
        IReadOnlyList<GraphNodeFact> nodes,
        IDictionary<(ImportantNodeTargetKind Kind, string StableId), TargetAccumulator> targets,
        bool includeGenerated,
        List<ImportantNodeExclusion> excluded)
    {
        foreach (var node in nodes.Where(static node => IsNamespace(node.NodeKind)).OrderBy(static node => node.StableId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(node.FilePath))
            {
                continue;
            }

            if (!includeGenerated && IsGeneratedPath(node.FilePath))
            {
                excluded.Add(new ImportantNodeExclusion(node.StableId, node.DisplayName, "generated-source"));
                continue;
            }

            Add(targets, ImportantNodeTargetKind.Namespace, node.StableId, node.DisplayName, node.FilePath, ImportantNodeEligibilityReason.NamespaceDeclaration);
        }
    }

    private static void AddPublicSymbols(
        IReadOnlyList<GraphSymbolFact> symbols,
        Dictionary<string, GraphNodeFact> nodesByStableId,
        IDictionary<(ImportantNodeTargetKind Kind, string StableId), TargetAccumulator> targets,
        bool includeGenerated,
        List<ImportantNodeExclusion> excluded)
    {
        foreach (var symbol in symbols
                     .Where(static symbol => string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static symbol => symbol.SymbolId, StringComparer.Ordinal))
        {
            if (!nodesByStableId.TryGetValue(symbol.NodeStableId, out var node) || string.IsNullOrWhiteSpace(node.FilePath))
            {
                continue;
            }

            if (!includeGenerated && IsGeneratedPath(node.FilePath))
            {
                excluded.Add(new ImportantNodeExclusion(symbol.SymbolId, symbol.FullyQualifiedName, "generated-source"));
                continue;
            }

            var kind = IsType(node.NodeKind)
                ? ImportantNodeTargetKind.PublicType
                : ImportantNodeTargetKind.PublicApi;
            var reason = kind == ImportantNodeTargetKind.PublicType
                ? ImportantNodeEligibilityReason.PublicTypeDeclaration
                : ImportantNodeEligibilityReason.PublicApiDeclaration;
            Add(targets, kind, node.StableId, symbol.FullyQualifiedName, node.FilePath, reason);
        }
    }

    private static void Add(
        IDictionary<(ImportantNodeTargetKind Kind, string StableId), TargetAccumulator> targets,
        ImportantNodeTargetKind kind,
        string stableId,
        string displayName,
        string sourcePath,
        ImportantNodeEligibilityReason reason)
    {
        var key = (kind, stableId);
        if (!targets.TryGetValue(key, out var target))
        {
            target = new TargetAccumulator(kind, stableId, displayName);
            targets.Add(key, target);
        }

        target.Add(sourcePath, reason);
    }

    private static bool IsNamespace(string nodeKind) =>
        string.Equals(nodeKind, "namespace", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(nodeKind, "semantic_namespace", StringComparison.OrdinalIgnoreCase);

    private static bool IsType(string nodeKind) =>
        nodeKind is "class" or "interface" or "struct" or "record" or "enum" or "delegate" ||
        string.Equals(nodeKind, "semantic_type", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                   .Any(static segment => string.Equals(segment, "generated", StringComparison.OrdinalIgnoreCase) || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)) ||
               fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeModulePath(string path) => path.Trim().Trim('/').Replace('\\', '/');

    private static bool IsSameOrDescendant(string path, string directory) =>
        string.Equals(path, directory, StringComparison.Ordinal) ||
        path.StartsWith(string.Concat(directory, "/"), StringComparison.Ordinal);

    private sealed class TargetAccumulator(ImportantNodeTargetKind kind, string stableId, string displayName)
    {
        private readonly SortedSet<string> sourcePaths = new(StringComparer.Ordinal);
        private readonly SortedSet<ImportantNodeEligibilityReason> reasons = new();

        public void Add(string sourcePath, ImportantNodeEligibilityReason reason)
        {
            sourcePaths.Add(sourcePath);
            reasons.Add(reason);
        }

        public ImportantNodeTarget ToTarget() => new(kind, stableId, displayName, [.. sourcePaths], [.. reasons]);
    }
}
