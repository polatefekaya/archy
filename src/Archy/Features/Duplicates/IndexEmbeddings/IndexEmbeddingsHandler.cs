using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Duplicates.IndexEmbeddings;
public sealed class IndexEmbeddingsHandler(IEmbeddingIndexSourceReader sourceReader, EmbeddingIndexer indexer, IEmbeddingCacheRepository cache, IEmbeddingModelProviderResolver providerResolver) : IRequestHandler<IndexEmbeddingsCommand, Result<EmbeddingIndexResult>>
{
    public async ValueTask<Result<EmbeddingIndexResult>> Handle(IndexEmbeddingsCommand command, CancellationToken cancellationToken)
    {
        var plan = await sourceReader.ReadAsync(command.Location, command.RepositoryRoot, command.Configuration, command.MaxChunks, cancellationToken);
        if (!plan.IsSuccess) return ResultFactory.Failure<EmbeddingIndexResult>(plan.Problem!);
        if (command.DryRun)
        {
            var hits = 0;
            foreach (var chunk in plan.Value.Chunks)
            {
                var cached = await cache.FindAsync(command.Location, new EmbeddingCacheKey(chunk.MethodStableId, command.ModelId, chunk.ContentHash), cancellationToken);
                if (!cached.IsSuccess) return ResultFactory.Failure<EmbeddingIndexResult>(cached.Problem!);
                if (cached.Value is not null) hits++;
            }
            return ResultFactory.Success(new EmbeddingIndexResult(command.ModelId, hits, 0, plan.Value.Chunks.Count, new ModelUsage(0, 0, 0, 0)));
        }
        var provider = providerResolver.Resolve(command.Configuration.Model.Provider);
        if (!provider.IsSuccess) return ResultFactory.Failure<EmbeddingIndexResult>(provider.Problem!);
        return await indexer.IndexAsync(new(command.Location, plan.Value.GraphRevision, command.Configuration, command.ModelId, plan.Value.Chunks, provider.Value), cancellationToken);
    }
}
