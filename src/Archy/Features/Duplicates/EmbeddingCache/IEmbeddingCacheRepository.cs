using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.EmbeddingCache;

public interface IEmbeddingCacheRepository
{
    ValueTask<Result<EmbeddingCacheEntry?>> FindAsync(WorkspaceStateLocation location, EmbeddingCacheKey key, CancellationToken cancellationToken);

    ValueTask<Result<EmbeddingCacheEntry>> StoreAsync(WorkspaceStateLocation location, EmbeddingCacheEntry entry, CancellationToken cancellationToken);
}
