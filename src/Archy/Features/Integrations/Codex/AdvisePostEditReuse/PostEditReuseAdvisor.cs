using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Similarity.ExplainReuseDecision;
using Archy.Features.Similarity.RetrieveHybridCandidates;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.AdvisePostEditReuse;

public sealed record PostEditReuseAdvice(long GraphRevision, bool GraphMayBeStale, IReadOnlyList<HybridSimilarityCandidate> Candidates, ReuseExplanation? StrongestExplanation, string? AbstentionReason);
public interface IPostEditReuseAdvisor { ValueTask<Result<PostEditReuseAdvice>> AdviseAsync(WorkspaceStateLocation location, IReadOnlyList<string> changedPaths, CancellationToken cancellationToken); }

/// <summary>Produces bounded structural reuse guidance after an edit. Persisted graph facts may predate the edit and are always labeled as such.</summary>
public sealed class PostEditReuseAdvisor(IGraphRevisionSnapshotReader snapshots) : IPostEditReuseAdvisor
{
    public async ValueTask<Result<PostEditReuseAdvice>> AdviseAsync(WorkspaceStateLocation location, IReadOnlyList<string> changedPaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(changedPaths);
        var safe = changedPaths.Where(IsSafePath).Distinct(StringComparer.Ordinal).Take(50).ToArray();
        if (safe.Length == 0) return ResultFactory.Success(new PostEditReuseAdvice(0, true, [], null, "No bounded repository-relative changed code paths were available."));
        var snapshot = await snapshots.ReadActiveAsync(location, cancellationToken); if (!snapshot.IsSuccess) return ResultFactory.Failure<PostEditReuseAdvice>(snapshot.Problem!);
        if (snapshot.Value is null) return ResultFactory.Success(new PostEditReuseAdvice(0, true, [], null, "No active graph revision exists; post-edit reuse guidance abstains."));
        var sources = snapshot.Value.Nodes.Where(node => node.FilePath is not null && safe.Contains(node.FilePath, StringComparer.Ordinal)).OrderBy(node => node.StableId, StringComparer.Ordinal).Take(10).ToArray();
        if (sources.Length == 0) return ResultFactory.Success(new PostEditReuseAdvice(snapshot.Value.Revision, true, [], null, "The persisted graph has no nodes for the changed paths; analyze the workspace to refresh graph evidence."));
        var retriever = new HybridSimilarityRetriever(); var suggestions = new List<(HybridSimilarityCandidate Candidate, ReuseExplanation Explanation)>();
        foreach (var source in sources)
        {
            var candidates = retriever.Retrieve(snapshot.Value, new(source.DisplayName + " " + source.CanonicalKey, source.StableId, source.FilePath, 10)).Candidates
                .Where(candidate => candidate.FilePath is not null && !string.Equals(candidate.FilePath, source.FilePath, StringComparison.Ordinal) && candidate.Confidence is SimilarityConfidence.High or SimilarityConfidence.Medium && candidate.Score >= .60d).Take(1);
            foreach (var candidate in candidates) suggestions.Add((candidate, new ReuseDecisionExplainer().Explain(snapshot.Value, new(candidate.StableId, source.StableId, source.DisplayName))));
        }
        var strongest = suggestions.OrderByDescending(item => item.Candidate.Score).ThenBy(item => item.Candidate.StableId, StringComparer.Ordinal).FirstOrDefault();
        return ResultFactory.Success(new PostEditReuseAdvice(snapshot.Value.Revision, true, suggestions.Select(item => item.Candidate).DistinctBy(candidate => candidate.StableId, StringComparer.Ordinal).Take(3).ToArray(), strongest.Candidate is null ? null : strongest.Explanation, strongest.Candidate is null ? "No high-confidence cross-file structural reuse candidate was found." : null));
    }
    private static bool IsSafePath(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathFullyQualified(path) && !path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal);
}
