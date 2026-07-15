using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Architecture.ArchitectureExceptions;

public sealed class ArchitectureExceptionPolicyRepositoryTests
{
    [Fact]
    public async Task AppendsAndRoundTripsAnAttributedTimeBoundDecision()
    {
        using var repository = TemporaryRepository.Create();
        var policyRepository = new ArchitectureExceptionPolicyRepository();
        var decision = Decision("exception:one", "finding:one");

        var appended = await policyRepository.AppendAsync(repository.Root, decision, CancellationToken.None);
        var read = await policyRepository.ReadAsync(repository.Root, CancellationToken.None);

        Assert.True(appended.IsSuccess, appended.IsSuccess ? string.Empty : appended.Problem!.Message);
        Assert.True(read.IsSuccess, read.IsSuccess ? string.Empty : read.Problem!.Message);
        Assert.Equal(Path.Combine(repository.Root, ArchitectureExceptionPolicyRepository.FileName), appended.Value);
        Assert.Equal(1, read.Value.Policy.SchemaVersion);
        Assert.Equal(decision, Assert.Single(read.Value.Policy.Exceptions));
    }

    [Fact]
    public async Task RejectsAnExceptionWithReviewAfterExpiry()
    {
        using var repository = TemporaryRepository.Create();
        var policyRepository = new ArchitectureExceptionPolicyRepository();
        var invalid = Decision("exception:invalid", "finding:one") with
        {
            ReviewAtUtc = DateTimeOffset.Parse("2026-08-01T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
        };

        var result = await policyRepository.AppendAsync(repository.Root, invalid, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
        Assert.False(File.Exists(Path.Combine(repository.Root, ArchitectureExceptionPolicyRepository.FileName)));
    }

    private static ArchitectureExceptionDecision Decision(string exceptionId, string findingKey) => new(
        exceptionId,
        findingKey,
        "fixture-author",
        "The architecture constraint is temporarily accepted while a migration is completed.",
        DateTimeOffset.Parse("2026-07-16T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-07-20T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-07-15T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));
}
