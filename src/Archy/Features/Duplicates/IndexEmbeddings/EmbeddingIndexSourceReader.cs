using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Memory.DetermineImportantNodes;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.IndexEmbeddings;

/// <summary>Builds bounded declaration chunks from the active graph; paths are resolved under the repository root before any file read.</summary>
public sealed class EmbeddingIndexSourceReader(IGraphRevisionSnapshotReader snapshots, IImportantNodeEligibilityPolicy eligibilityPolicy, IEmbeddingChunkSelector selector) : IEmbeddingIndexSourceReader
{
    private const long MaxSourceFileBytes = 2_000_000;
    public async ValueTask<Result<EmbeddingIndexPlan>> ReadAsync(WorkspaceStateLocation location, string repositoryRoot, ArchyConfiguration configuration, int maxChunks, CancellationToken cancellationToken)
    {
        if (maxChunks is < 1 or > 2048) return ResultFactory.Failure<EmbeddingIndexPlan>(Problem.Validation("Embedding chunk limit must be from 1 through 2048."));
        var snapshot = await snapshots.ReadActiveAsync(location, cancellationToken);
        if (!snapshot.IsSuccess) return ResultFactory.Failure<EmbeddingIndexPlan>(snapshot.Problem!);
        if (snapshot.Value is null) return ResultFactory.Failure<EmbeddingIndexPlan>(Problem.Conflict("No active graph exists. Run 'archy analyze' before indexing embeddings."));
        var root = Path.GetFullPath(repositoryRoot);
        var sources = new List<EmbeddingChunkSource>();
        // Language servers do not all expose method facts.  Public type declarations are
        // nevertheless durable, useful semantic units, so source acquisition must stay
        // independent of a particular provider's symbol granularity.
        foreach (var node in snapshot.Value.Nodes.Where(HasSourceRange).OrderBy(static node => node.StableId, StringComparer.Ordinal))
        {
            if (node.FilePath is null || node.StartLine is not > 0 || node.EndLine < node.StartLine) continue;
            var file = Path.GetFullPath(Path.Combine(root, node.FilePath));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file) || new FileInfo(file).Length > MaxSourceFileBytes) continue;
            try { sources.Add(new(node.StableId, node.FilePath.Replace('\\', '/'), node.StartLine.GetValueOrDefault(), node.EndLine.GetValueOrDefault(), await File.ReadAllTextAsync(file, cancellationToken))); }
            catch (IOException) { /* A concurrently changed file simply cannot participate in this immutable plan. */ }
        }
        try
        {
            var chunks = selector.CreateChunks(eligibilityPolicy.Determine(snapshot.Value, configuration.Memory), sources).Take(maxChunks).ToArray();
            return ResultFactory.Success(new EmbeddingIndexPlan(snapshot.Value.Revision, chunks));
        }
        catch (ArgumentException exception) { return ResultFactory.Failure<EmbeddingIndexPlan>(Problem.Validation(exception.Message)); }
    }
    private static bool HasSourceRange(Archy.Features.Graph.CommitGraphRevision.GraphNodeFact node) =>
        node.FilePath is not null && node.StartLine is > 0 && node.EndLine >= node.StartLine;
}
