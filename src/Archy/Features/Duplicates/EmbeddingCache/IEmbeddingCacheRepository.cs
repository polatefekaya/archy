using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.EmbeddingCache;

public interface IEmbeddingCacheRepository
{
    ValueTask<Result<EmbeddingCacheEntry?>> FindAsync(WorkspaceStateLocation location, EmbeddingCacheKey key, CancellationToken cancellationToken);

    ValueTask<Result<EmbeddingCacheEntry>> StoreAsync(WorkspaceStateLocation location, EmbeddingCacheEntry entry, CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<EmbeddingCacheEntry>>> ListByModelAsync(WorkspaceStateLocation location, string modelId, int limit, CancellationToken cancellationToken);

    ValueTask<Result<EmbeddingCacheStatistics>> ReadStatisticsAsync(WorkspaceStateLocation location, string modelId, CancellationToken cancellationToken);
}
