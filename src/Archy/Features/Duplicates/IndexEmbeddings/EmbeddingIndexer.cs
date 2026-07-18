using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Memory.AuthorizeAiSourceSharing;
using Archy.Features.Memory.GovernModelRequests;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.IndexEmbeddings;

/// <summary>Explicit outbound boundary: consent, admission control, and provider identity are checked before source leaves the machine.</summary>
public sealed class EmbeddingIndexer(
    IRepositoryAiConsentPolicy consent,
    IEmbeddingCacheResolver resolver,
    IModelRequestGovernor governor)
{
    public async ValueTask<Result<EmbeddingIndexResult>> IndexAsync(EmbeddingIndexRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var permission = consent.Evaluate(request.Configuration.Memory, AiSourceSharingOperation.Embedding);
        if (!permission.IsAllowed) return ResultFactory.Failure<EmbeddingIndexResult>(Problem.Conflict(permission.Reason));
        if (request.GraphRevision < 1 || string.IsNullOrWhiteSpace(request.ModelId) || !request.Provider.Descriptor.SupportsEmbeddings ||
            !string.Equals(request.Configuration.Model.Provider, request.Provider.Descriptor.ProviderId, StringComparison.OrdinalIgnoreCase))
            return ResultFactory.Failure<EmbeddingIndexResult>(Problem.Validation("A configured embedding-capable provider and model are required."));

        var chunks = request.Chunks.Take(Math.Clamp(request.Configuration.Model.MaxRequestsPerRun, 1, 2048)).ToArray();
        if (chunks.Length == 0) return ResultFactory.Success(new EmbeddingIndexResult(request.ModelId, 0, 0, 0, new ModelUsage(0, 0, 0, 0)));
        var tokens = Math.Min(request.Configuration.Model.MaxTokensPerRun, Math.Max(1, chunks.Sum(static chunk => (chunk.Content.Length + 3) / 4)));
        var decision = governor.TryReserve($"embedding:{request.Location.WorkspaceId}:{request.GraphRevision}", $"{request.ModelId}:{request.GraphRevision}", new ModelRequestEstimate(tokens, 0), request.Configuration.Model);
        if (!decision.IsGranted) return ResultFactory.Failure<EmbeddingIndexResult>(Problem.Conflict($"Embedding request was not admitted: {decision.Denial}."));

        try
        {
            var resolved = await resolver.ResolveAsync(request.Location, request.GraphRevision, request.ModelId, chunks, request.Provider, cancellationToken);
            if (resolved.IsSuccess) return ResultFactory.Success(new EmbeddingIndexResult(resolved.Value!.ModelId, resolved.Value.CacheHitCount, resolved.Value.GeneratedCount, chunks.Length, resolved.Value.GeneratedUsage));
            if (resolved.Failure!.Kind == ModelProviderFailureKind.RateLimited) governor.ApplyRateLimitCooldown(resolved.Failure.RetryAfter, request.Configuration.Model);
            return ResultFactory.Failure<EmbeddingIndexResult>(Problem.Storage(resolved.Failure.Message));
        }
        finally { governor.Complete(decision.Lease!); }
    }
}
