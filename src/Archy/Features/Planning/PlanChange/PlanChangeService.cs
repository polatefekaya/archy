using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Planning.AnalyzeImpact;
using Archy.Features.Similarity.ExplainReuseDecision;
using Archy.Features.Similarity.RetrieveHybridCandidates;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Planning.PlanChange;

public enum ChangeRecommendation { Reuse, Extend, NewCapability, InsufficientEvidence }
public sealed record ChangePlanRequest(string Description, IReadOnlyList<string>? IntendedFiles = null, string? TargetStableId = null, string? IntendedModule = null, string? Snippet = null);
public sealed record ChangePlanCandidate(HybridSimilarityCandidate Candidate, ReuseExplanation Explanation);
public sealed record ChangePlan(long GraphRevision, ChangeRecommendation Recommendation, IReadOnlyList<ChangePlanCandidate> Candidates, ImpactAnalysisResult? Impact, IReadOnlyList<string> ExistingFiles, IReadOnlyList<string> SuggestedNewFiles, IReadOnlyList<string> DecisionIds, IReadOnlyList<string> ValidationSteps, IReadOnlyList<string> Abstentions);
public interface IChangePlanner { ValueTask<Result<ChangePlan>> PlanAsync(WorkspaceStateLocation location, ChangePlanRequest request, CancellationToken cancellationToken); }

public sealed class ChangePlanner(IGraphRevisionSnapshotReader snapshotReader, IImpactAnalyzer impactAnalyzer) : IChangePlanner
{
    public async ValueTask<Result<ChangePlan>> PlanAsync(WorkspaceStateLocation location, ChangePlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 10_000 || request.Snippet?.Length > 100_000 || request.IntendedFiles?.Count > 50 || request.IntendedFiles?.Any(path => !IsSafeRelativePath(path)) == true) return ResultFactory.Failure<ChangePlan>(Problem.Validation("Change planning requires a bounded description, snippet, and repository-relative intended paths without traversal."));
        var snapshot = await snapshotReader.ReadActiveAsync(location, cancellationToken); if (!snapshot.IsSuccess) return ResultFactory.Failure<ChangePlan>(snapshot.Problem!); if (snapshot.Value is null) return ResultFactory.Success(new ChangePlan(0, ChangeRecommendation.InsufficientEvidence, [], null, [], [], [], ["archy analyze", "archy verify --path ."], ["No active graph revision exists."]));
        if (request.TargetStableId is not null && !snapshot.Value.Nodes.Any(node => node.StableId == request.TargetStableId)) return ResultFactory.Success(new ChangePlan(snapshot.Value.Revision, ChangeRecommendation.InsufficientEvidence, [], null, [], [], [], ["archy analyze", "archy verify --path ."], ["The requested target stable ID does not exist in the active graph."]));
        var intendedPath = request.IntendedFiles is { Count: > 0 } ? request.IntendedFiles[0] : null;
        var hybrid = new HybridSimilarityRetriever().Retrieve(snapshot.Value, new(request.Description + " " + request.Snippet, request.TargetStableId, intendedPath, 20));
        var explainer = new ReuseDecisionExplainer(); var candidates = hybrid.Candidates.Take(3).Select(candidate => new ChangePlanCandidate(candidate, explainer.Explain(snapshot.Value, new(candidate.StableId, request.TargetStableId, request.Description)))).ToArray();
        ImpactAnalysisResult? impact = null; if (request.TargetStableId is not null) { var analyzed = await impactAnalyzer.AnalyzeAsync(location, new(request.TargetStableId), cancellationToken); if (analyzed.IsSuccess) impact = analyzed.Value; }
        var recommendation = candidates.Select(candidate => candidate.Explanation.Recommendation).Contains(ReuseRecommendation.Reuse) ? ChangeRecommendation.Reuse : candidates.Select(candidate => candidate.Explanation.Recommendation).Contains(ReuseRecommendation.Extend) ? ChangeRecommendation.Extend : candidates.Length > 0 ? ChangeRecommendation.NewCapability : ChangeRecommendation.InsufficientEvidence;
        var existing = request.IntendedFiles?.Where(path => snapshot.Value.Nodes.Any(node => string.Equals(node.FilePath, path, StringComparison.Ordinal))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
        var suggested = request.IntendedFiles?.Except(existing, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
        var abstentions = new List<string>(); if (hybrid.Abstained) abstentions.Add(hybrid.AbstentionReason!); if (impact?.Abstained == true) abstentions.Add(impact.AbstentionReason!);
        IReadOnlyList<string> decisions = [];
        if (request.TargetStableId is not null)
        {
            var decisionRead = await new ArchitectureDecisionRepository(TimeProvider.System, new Archy.Features.Workspaces.AcquireWorkspaceLock.WorkspaceLockManager(TimeProvider.System)).ListForTargetAsync(location, new(Archy.SharedKernel.Primitives.ArchitectureTargetKind.GraphNode, request.TargetStableId), cancellationToken);
            if (decisionRead.IsSuccess) decisions = decisionRead.Value!.Select(decision => decision.DecisionId).Take(20).ToArray();
            else abstentions.Add("Architecture decisions could not be read for the requested target.");
        }
        return ResultFactory.Success(new ChangePlan(snapshot.Value.Revision, recommendation, candidates, impact, existing, suggested, decisions, ["archy analyze", "archy verify --path ."], abstentions));
    }
    private static bool IsSafeRelativePath(string? path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathFullyQualified(path) && !path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal);
}
