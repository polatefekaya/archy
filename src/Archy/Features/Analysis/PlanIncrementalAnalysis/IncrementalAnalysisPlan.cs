using Archy.Features.Analysis.InventorySources;

namespace Archy.Features.Analysis.PlanIncrementalAnalysis;

/// <summary>
/// A deterministic work set for one immutable inventory snapshot. Semantic LSP sessions remain
/// full snapshots until their protocol has a separately verified incremental synchronization mode.
/// </summary>
public sealed record IncrementalAnalysisPlan(
    IncrementalAnalysisPlanReason Reason,
    bool ReusesCSharpSyntaxFacts,
    bool RequiresFullSemanticSnapshot,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<SourceFile> CSharpFilesToParse,
    IReadOnlyList<string> AffectedStableIds,
    IReadOnlyList<string> DirectDependentPaths,
    IReadOnlyList<string> AffectedProviderIds,
    IReadOnlyList<string> AffectedJoinKeys);
