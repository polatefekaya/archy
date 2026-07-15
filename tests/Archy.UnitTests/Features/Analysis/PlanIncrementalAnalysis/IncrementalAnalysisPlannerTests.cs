using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.PlanIncrementalAnalysis;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.UnitTests.Features.Analysis.PlanIncrementalAnalysis;

public sealed class IncrementalAnalysisPlannerTests
{
    [Fact]
    public void CreateReusesSyntaxFactsForOneChangedFileAndIncludesDirectDependents()
    {
        var changed = File("src/Changed.cs", "hash:changed:v2");
        var unchanged = File("src/Dependent.cs", "hash:dependent:v1");
        var inventory = Inventory(
            [changed, unchanged],
            [new SourceFileChange(SourceFileChangeKind.Changed, changed), new SourceFileChange(SourceFileChangeKind.Unchanged, unchanged)]);
        var changedFileNode = Node("csharp:file:src/Changed.cs", "file", "src/Changed.cs");
        var changedTypeNode = Node("csharp:type:Changed", "class", "src/Changed.cs");
        var dependentFileNode = Node("csharp:file:src/Dependent.cs", "file", "src/Dependent.cs");
        var dependentTypeNode = Node("csharp:type:Dependent", "class", "src/Dependent.cs");
        var snapshot = new GraphRevisionSnapshot(
            4,
            [changedFileNode, changedTypeNode, dependentFileNode, dependentTypeNode],
            [new GraphEdgeFact("edge:dependent-changed", dependentTypeNode.StableId, changedTypeNode.StableId, "calls", "join:changed", "semantic", 1, "{}")],
            [],
            []);

        var plan = new IncrementalAnalysisPlanner().Create(inventory, snapshot);

        Assert.Equal(IncrementalAnalysisPlanReason.IncrementalReuse, plan.Reason);
        Assert.True(plan.ReusesCSharpSyntaxFacts);
        Assert.True(plan.RequiresFullSemanticSnapshot);
        Assert.Equal(["src/Changed.cs"], plan.CSharpFilesToParse.Select(static file => file.RepositoryRelativePath));
        Assert.Equal(["src/Dependent.cs"], plan.DirectDependentPaths);
        Assert.Contains(changedTypeNode.StableId, plan.AffectedStableIds);
        Assert.Contains(dependentTypeNode.StableId, plan.AffectedStableIds);
        Assert.Equal(["semantic"], plan.AffectedProviderIds);
        Assert.Equal(["join:changed"], plan.AffectedJoinKeys);
    }

    [Fact]
    public void CreateFallsBackToFullSyntaxScanWhenActiveSnapshotCannotCoverAnUnchangedCSharpFile()
    {
        var changed = File("src/Changed.cs", "hash:changed:v2");
        var unchanged = File("src/Uncovered.cs", "hash:uncovered:v1");
        var inventory = Inventory(
            [changed, unchanged],
            [new SourceFileChange(SourceFileChangeKind.Changed, changed), new SourceFileChange(SourceFileChangeKind.Unchanged, unchanged)]);
        var snapshot = new GraphRevisionSnapshot(
            4,
            [Node("csharp:file:src/Changed.cs", "file", "src/Changed.cs")],
            [],
            [],
            []);

        var plan = new IncrementalAnalysisPlanner().Create(inventory, snapshot);

        Assert.Equal(IncrementalAnalysisPlanReason.SnapshotCoverageUnavailable, plan.Reason);
        Assert.False(plan.ReusesCSharpSyntaxFacts);
        Assert.Equal(["src/Changed.cs", "src/Uncovered.cs"], plan.CSharpFilesToParse.Select(static file => file.RepositoryRelativePath));
    }

    private static SourceInventory Inventory(IReadOnlyList<SourceFile> files, IReadOnlyList<SourceFileChange> changes) =>
        new(true, files, changes, [], [], []);

    private static SourceFile File(string path, string hash) => new(path, SourceLanguage.CSharp, hash, 10);

    private static GraphNodeFact Node(string stableId, string kind, string filePath) =>
        new(stableId, kind, stableId, stableId, filePath, 1, 1, "csharp-syntax", 1, "{}", "hash");
}
