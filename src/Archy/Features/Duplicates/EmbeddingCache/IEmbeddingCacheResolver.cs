using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Duplicates.EmbeddingCache;

public interface IEmbeddingCacheResolver
{
    /// <remarks>The caller must enforce source-sharing consent and request budgets before invoking a provider.</remarks>
    ValueTask<ModelProviderResult<EmbeddingCacheResolution>> ResolveAsync(
        WorkspaceStateLocation location,
        long graphRevision,
        string modelId,
        IReadOnlyList<EmbeddingChunk> chunks,
        IModelProvider provider,
        CancellationToken cancellationToken);
}
