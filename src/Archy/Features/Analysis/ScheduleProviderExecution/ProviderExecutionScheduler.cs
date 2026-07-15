using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ScheduleProviderExecution;

public sealed class ProviderExecutionScheduler : IProviderExecutionScheduler
{
    public async ValueTask<Result<ProviderExecutionBatch>> ExecuteAsync(
        ProviderExecutionSnapshot snapshot,
        IReadOnlyList<IProviderSiteMatcher> matchers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(matchers);
        if (string.IsNullOrWhiteSpace(snapshot.RepositoryRoot) ||
            string.IsNullOrWhiteSpace(snapshot.SnapshotId) ||
            snapshot.Files is null)
        {
            return ResultFactory.Failure<ProviderExecutionBatch>(Problem.Validation("Provider execution requires an immutable snapshot with repository root, files, and identity."));
        }

        if (matchers.Any(static matcher => matcher is null || string.IsNullOrWhiteSpace(matcher.ProviderId)) ||
            matchers.Select(static matcher => matcher.ProviderId).Distinct(StringComparer.Ordinal).Count() != matchers.Count)
        {
            return ResultFactory.Failure<ProviderExecutionBatch>(Problem.Validation("Provider execution requires uniquely identified matchers."));
        }

        var results = new List<ProviderExecutionResult>(matchers.Count);
        foreach (var matcher in matchers.OrderBy(static matcher => matcher.ProviderId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var matched = await matcher.MatchAsync(snapshot.RepositoryRoot, snapshot.Files, cancellationToken);
                if (!matched.IsSuccess)
                {
                    results.Add(new ProviderExecutionResult(
                        matcher.ProviderId,
                        ProviderExecutionState.Degraded,
                        [],
                        new ProviderExecutionDiagnostic(matched.Problem!.Code, matched.Problem.Message)));
                    continue;
                }

                results.Add(new ProviderExecutionResult(
                    matcher.ProviderId,
                    ProviderExecutionState.Succeeded,
                    [.. matched.Value],
                    Diagnostic: null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new ProviderExecutionResult(
                    matcher.ProviderId,
                    ProviderExecutionState.Degraded,
                    [],
                    new ProviderExecutionDiagnostic("provider_exception", $"Provider '{matcher.ProviderId}' failed: {exception.Message}")));
            }
        }

        return ResultFactory.Success(new ProviderExecutionBatch(snapshot.SnapshotId, results));
    }
}
