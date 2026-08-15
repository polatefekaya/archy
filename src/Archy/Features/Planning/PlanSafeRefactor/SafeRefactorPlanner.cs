using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Planning.PlanSafeRefactor;

public enum RefactorIntent { Move, Split, Merge, Rename, Extract, Replace }
public sealed record SafeRefactorRequest(string TargetStableId, RefactorIntent Intent, string? DestinationPath = null);
public sealed record RefactorCheckpoint(string Phase, string ExpectedState, string ArchyCheck);
public sealed record SafeRefactorPlan(long GraphRevision, string TargetStableId, RefactorIntent Intent, IReadOnlyList<RefactorCheckpoint> Checkpoints, IReadOnlyList<string> CompatibilityGuidance, IReadOnlyList<string> Abstentions);
public interface ISafeRefactorPlanner { ValueTask<Result<SafeRefactorPlan>> PlanAsync(WorkspaceStateLocation location, SafeRefactorRequest request, CancellationToken cancellationToken); }

public sealed class SafeRefactorPlanner(IGraphRevisionSnapshotReader snapshotReader, IImpactAnalyzer impactAnalyzer) : ISafeRefactorPlanner
{
    public async ValueTask<Result<SafeRefactorPlan>> PlanAsync(WorkspaceStateLocation location, SafeRefactorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TargetStableId) || !Enum.IsDefined(request.Intent) || request.DestinationPath is { } destination && (!IsSafe(destination) || destination.Length > 1000)) return ResultFactory.Failure<SafeRefactorPlan>(Problem.Validation("Safe refactor planning requires a target, supported intent, and a safe repository-relative destination path."));
        var snapshot = await snapshotReader.ReadActiveAsync(location, cancellationToken); if (!snapshot.IsSuccess) return ResultFactory.Failure<SafeRefactorPlan>(snapshot.Problem!); if (snapshot.Value is null) return ResultFactory.Success(new SafeRefactorPlan(0, request.TargetStableId, request.Intent, [], [], ["No active graph revision exists."]));
        var node = snapshot.Value.Nodes.SingleOrDefault(item => item.StableId == request.TargetStableId); if (node is null) return ResultFactory.Success(new SafeRefactorPlan(snapshot.Value.Revision, request.TargetStableId, request.Intent, [], [], ["The target does not exist in the active graph revision."]));
        var impact = await impactAnalyzer.AnalyzeAsync(location, new(request.TargetStableId), cancellationToken); var publicSurface = snapshot.Value.Symbols.Any(symbol => symbol.NodeStableId == node.StableId && string.Equals(symbol.Visibility, "public", StringComparison.OrdinalIgnoreCase));
        var checkpoints = new[] { new RefactorCheckpoint("baseline", "Active graph and deterministic findings are recorded before edits.", "archy verify --path ."), new RefactorCheckpoint("isolate", "The target’s direct dependency and dependent set is understood.", "impact_analysis"), new RefactorCheckpoint("migrate callers", "Reachable callers use the intended replacement or compatibility surface.", "get_dependents"), new RefactorCheckpoint("preserve compatibility", publicSurface ? "Public callers retain a compatible adapter, deprecation path, or parallel contract." : "No persisted public surface requires compatibility handling.", "archy verify --path ."), new RefactorCheckpoint("remove old path", "The old declaration is removed only after callers have migrated.", "find_reintroduced"), new RefactorCheckpoint("verify", "Graph facts and deterministic architecture checks remain clean.", "archy analyze && archy verify --path .") };
        var guidance = publicSurface ? new[] { "Preserve the existing public surface through an adapter, deprecation, or parallel contract until dependents migrate." } : [];
        var abstentions = impact.IsSuccess && impact.Value!.IsTruncated ? new[] { "Impact traversal was truncated; inspect additional dependents before removal." } : [];
        return ResultFactory.Success(new SafeRefactorPlan(snapshot.Value.Revision, node.StableId, request.Intent, checkpoints, guidance, abstentions));
    }
    private static bool IsSafe(string value) => !Path.IsPathFullyQualified(value) && !value.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal);
}
