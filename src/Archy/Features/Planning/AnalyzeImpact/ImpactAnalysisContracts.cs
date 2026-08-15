using Archy.Features.Graph.TraverseDependencies;

namespace Archy.Features.Planning.AnalyzeImpact;

public enum ImpactDirection { Dependents, Dependencies, Both }
public sealed record ImpactAnalysisRequest(string TargetStableId, ImpactDirection Direction = ImpactDirection.Both, int Depth = 3, int MaxNodes = 100, IReadOnlyList<string>? EdgeKinds = null);
public sealed record ImpactPath(string StableId, int Depth, string ViaEdgeId, string EdgeKind, GraphTraversalDirection Direction);
public sealed record ImpactRisk(string Id, string Detail, bool Deterministic);
public sealed record ImpactAnalysisResult(long GraphRevision, string TargetStableId, IReadOnlyList<ImpactPath> Direct, IReadOnlyList<ImpactPath> Transitive, IReadOnlyList<string> PublicSurfaceStableIds, IReadOnlyList<ImpactRisk> Risks, bool IsTruncated, bool Abstained, string? AbstentionReason);
public interface IImpactAnalyzer { ValueTask<Archy.SharedKernel.Primitives.Result<ImpactAnalysisResult>> AnalyzeAsync(Archy.Features.Workspaces.InitializeWorkspace.WorkspaceStateLocation location, ImpactAnalysisRequest request, CancellationToken cancellationToken); }
