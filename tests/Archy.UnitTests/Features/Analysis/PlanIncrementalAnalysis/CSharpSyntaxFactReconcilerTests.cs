using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.PlanIncrementalAnalysis;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;

namespace Archy.UnitTests.Features.Analysis.PlanIncrementalAnalysis;

public sealed class CSharpSyntaxFactReconcilerTests
{
    [Fact]
    public void ReconcileClosesDeletedFileFactsWhilePreservingUnchangedSyntaxFacts()
    {
        var oldFile = Node("csharp:file:src/Old.cs", "file", "src/Old.cs");
        var oldType = Node("csharp:type:Old", "class", "src/Old.cs");
        var keptFile = Node("csharp:file:src/Kept.cs", "file", "src/Kept.cs");
        var keptType = Node("csharp:type:Kept", "class", "src/Kept.cs");
        var reference = new GraphNodeFact("csharp:using-reference:System", "unresolved_reference", "csharp:using:System", "System", null, null, null, "csharp-syntax", .65, "{}", "hash:system");
        var snapshot = new GraphRevisionSnapshot(
            7,
            [oldFile, oldType, keptFile, keptType, reference],
            [
                Edge("edge:old", oldFile.StableId, reference.StableId),
                Edge("edge:kept", keptFile.StableId, reference.StableId),
            ],
            [
                new GraphSymbolFact("symbol:old", oldType.StableId, "Old", "public", "Old", "[]", "{}", "old"),
                new GraphSymbolFact("symbol:kept", keptType.StableId, "Kept", "public", "Kept", "[]", "{}", "kept"),
            ],
            [new InterfaceFingerprintFact("symbol:kept", "public_surface", "kept", "[]")]);
        var deleted = new SourceFile("src/Old.cs", SourceLanguage.CSharp, "old", 1);
        var plan = new IncrementalAnalysisPlan(
            IncrementalAnalysisPlanReason.IncrementalReuse,
            ReusesCSharpSyntaxFacts: true,
            RequiresFullSemanticSnapshot: true,
            ChangedPaths: ["src/Old.cs"],
            DeletedPaths: ["src/Old.cs"],
            CSharpFilesToParse: [],
            AffectedStableIds: [oldFile.StableId, oldType.StableId],
            DirectDependentPaths: [],
            AffectedProviderIds: [],
            AffectedJoinKeys: []);

        var result = new CSharpSyntaxFactReconciler().Reconcile(
            snapshot,
            plan,
            new CSharpSyntaxFactBatch(true, [], [], [], [], []));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Equal([keptFile.StableId, keptType.StableId, reference.StableId], result.Value.Nodes.Select(static node => node.StableId));
        Assert.Equal(["edge:kept"], result.Value.Edges.Select(static edge => edge.EdgeId));
        Assert.Equal(["symbol:kept"], result.Value.Symbols.Select(static symbol => symbol.SymbolId));
        Assert.Equal(["symbol:kept"], result.Value.InterfaceFingerprints.Select(static fingerprint => fingerprint.SymbolId));
    }

    private static GraphNodeFact Node(string stableId, string kind, string path) =>
        new(stableId, kind, stableId, stableId, path, 1, 1, "csharp-syntax", 1, "{}", "hash");

    private static GraphEdgeFact Edge(string edgeId, string source, string target) =>
        new(edgeId, source, target, "using", null, "csharp-syntax", .65, "{}");
}
