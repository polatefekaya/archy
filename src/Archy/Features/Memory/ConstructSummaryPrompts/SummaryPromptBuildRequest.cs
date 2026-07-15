using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Memory.ComparePublicSurface;
using Archy.Features.Memory.DetermineImportantNodes;
using Archy.Features.Memory.SummaryBatches;

namespace Archy.Features.Memory.ConstructSummaryPrompts;

public sealed record SummaryPromptBuildRequest(
    SummaryBatch Batch,
    ImportantNodeTarget Target,
    IReadOnlyList<SummaryPromptSource> Sources,
    string? PriorSummary,
    PublicSurfaceDiff? PublicSurfaceDiff,
    GraphRevisionSnapshot Graph,
    int MaxSourceCharacters = 12_000,
    int MaxRelatedEdges = 50);
