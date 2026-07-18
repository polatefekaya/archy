using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Duplicates.IndexEmbeddings;

public sealed class EmbeddingIndexStatusHandler(
    IEmbeddingCacheRepository cache,
    IGraphRevisionSnapshotReader snapshots)
    : IRequestHandler<EmbeddingIndexStatusQuery, Result<EmbeddingIndexStatus>>
{
    public async ValueTask<Result<EmbeddingIndexStatus>> Handle(
        EmbeddingIndexStatusQuery query,
        CancellationToken cancellationToken)
    {
        var statistics = await cache.ReadStatisticsAsync(query.Location, query.ModelId, cancellationToken);
        if (!statistics.IsSuccess)
        {
            return ResultFactory.Failure<EmbeddingIndexStatus>(statistics.Problem!);
        }

        var graph = await snapshots.ReadActiveAsync(query.Location, cancellationToken);
        if (!graph.IsSuccess)
        {
            return ResultFactory.Failure<EmbeddingIndexStatus>(graph.Problem!);
        }

        return ResultFactory.Success(new EmbeddingIndexStatus(
            query.ModelId,
            graph.Value?.Revision,
            statistics.Value.Count,
            statistics.Value.Dimensions,
            statistics.Value.LatestCreatedAtUtc,
            query.Configuration.Model.Provider,
            query.Configuration.Memory.SourceSharing.ToString()));
    }
}
