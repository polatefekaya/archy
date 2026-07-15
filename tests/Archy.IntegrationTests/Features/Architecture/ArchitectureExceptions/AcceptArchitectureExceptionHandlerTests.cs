using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.VerifyArchitecture;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Architecture.ArchitectureExceptions;

public sealed class AcceptArchitectureExceptionHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-07-15T12:00:00+00:00",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task AppendsAnExceptionOnlyForOneCurrentIntroducedFinding()
    {
        using var repository = TemporaryRepository.Create();
        var finding = Finding("finding:introduced");
        var handler = new AcceptArchitectureExceptionHandler(
            new FixedVerificationService(Verification(finding, ArchitectureFindingStatus.Introduced)),
            new WorkspaceLocator(),
            new ArchitectureExceptionPolicyRepository(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new AcceptArchitectureExceptionCommand(
                repository.Root,
                null,
                null,
                finding.Key,
                "fixture-author",
                "A migration is in progress and has an approved temporary boundary.",
                Now.AddDays(1),
                Now.AddDays(7)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Equal(finding.Key, result.Value.FindingKey);
        var policy = await new ArchitectureExceptionPolicyRepository().ReadAsync(repository.Root, CancellationToken.None);
        Assert.True(policy.IsSuccess);
        var decision = Assert.Single(policy.Value.Policy.Exceptions);
        Assert.Equal(result.Value.ExceptionId, decision.ExceptionId);
        Assert.Equal("fixture-author", decision.Author);
        Assert.Equal(Now.AddDays(7), decision.ExpiresAtUtc);
    }

    [Fact]
    public async Task RejectsLegacyOrResolvedFindingsBeforeWritingAnyPolicy()
    {
        using var repository = TemporaryRepository.Create();
        var finding = Finding("finding:legacy");
        var handler = new AcceptArchitectureExceptionHandler(
            new FixedVerificationService(Verification(finding, ArchitectureFindingStatus.Legacy)),
            new WorkspaceLocator(),
            new ArchitectureExceptionPolicyRepository(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            new AcceptArchitectureExceptionCommand(
                repository.Root,
                null,
                null,
                finding.Key,
                "fixture-author",
                "This must not create an unnecessary exception.",
                Now.AddDays(1),
                Now.AddDays(7)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Problem!.Code);
        Assert.False(File.Exists(Path.Combine(repository.Root, ArchitectureExceptionPolicyRepository.FileName)));
    }

    private static ArchitectureVerification Verification(
        ArchitectureFinding finding,
        ArchitectureFindingStatus status) => new(
        1,
        true,
        "rules:fixture",
        new LayerDependencyEvaluation([], [], [], []),
        new ArchitectureBaselineComparison(
            ArchitectureBaselineStatus.Compatible,
            "/repo/archy.baseline.json",
            1,
            [new ArchitectureFindingOutcome(finding, status)]),
        [],
        []);

    private static ArchitectureFinding Finding(string key) => new(
        key,
        ArchitectureFindingKind.LayerDependency,
        "Fixture architecture finding.",
        [new ArchitectureTarget(ArchitectureTargetKind.Rule, "fixture-rule")]);

    private sealed class FixedVerificationService(ArchitectureVerification verification) : IArchitectureVerificationService
    {
        public ValueTask<Result<ArchitectureVerification>> VerifyAsync(
            VerifyArchitectureCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ResultFactory.Success(verification));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
