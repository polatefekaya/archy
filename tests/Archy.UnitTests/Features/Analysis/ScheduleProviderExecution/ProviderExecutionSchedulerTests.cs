using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.ProviderSiteMatches;
using Archy.Features.Analysis.ScheduleProviderExecution;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Analysis.ScheduleProviderExecution;

public sealed class ProviderExecutionSchedulerTests
{
    [Fact]
    public async Task ExecuteIsolatesAProviderCrashAndPreservesOtherProviderFacts()
    {
        var snapshot = new ProviderExecutionSnapshot("/repo", [], "snapshot-1");
        var result = await new ProviderExecutionScheduler().ExecuteAsync(
            snapshot,
            [new ThrowingMatcher(), new SuccessfulMatcher()],
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("snapshot-1", result.Value.SnapshotId);
        var failed = Assert.Single(result.Value.Providers, static provider => provider.ProviderId == "broken");
        Assert.Equal(ProviderExecutionState.Degraded, failed.State);
        Assert.Equal("provider_exception", failed.Diagnostic!.Code);
        var succeeded = Assert.Single(result.Value.Providers, static provider => provider.ProviderId == "healthy");
        Assert.Equal(ProviderExecutionState.Succeeded, succeeded.State);
        Assert.Single(succeeded.Matches);
    }

    private sealed class ThrowingMatcher : IProviderSiteMatcher
    {
        public string ProviderId => "broken";

        public ValueTask<Result<IReadOnlyList<ProviderSiteMatch>>> MatchAsync(string repositoryRoot, IReadOnlyList<SourceFile> files, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fixture fault");
    }

    private sealed class SuccessfulMatcher : IProviderSiteMatcher
    {
        public string ProviderId => "healthy";

        public ValueTask<Result<IReadOnlyList<ProviderSiteMatch>>> MatchAsync(string repositoryRoot, IReadOnlyList<SourceFile> files, CancellationToken cancellationToken)
        {
            var match = ProviderSiteMatchContract.Create(
                ProviderId,
                "csharp",
                framework: null,
                "fixture",
                new ProviderSiteEvidence("src/Fixture.cs", "ABC", 1, 1, 1, 7),
                [],
                ProviderSiteMatchState.Matched,
                diagnostic: null);
            return ValueTask.FromResult(ResultFactory.Success<IReadOnlyList<ProviderSiteMatch>>([match.Value]));
        }
    }
}
