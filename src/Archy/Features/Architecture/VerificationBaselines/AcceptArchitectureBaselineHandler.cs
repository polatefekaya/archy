using Archy.Features.Architecture.VerifyArchitecture;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Creates a portable accepted-finding set only after a complete current verification.</summary>
public sealed class AcceptArchitectureBaselineHandler(
    IArchitectureVerificationService verificationService,
    IWorkspaceLocator workspaceLocator,
    IArchitectureBaselinePolicyRepository baselineRepository,
    TimeProvider timeProvider)
    : IRequestHandler<AcceptArchitectureBaselineCommand, Result<AcceptedArchitectureBaseline>>
{
    public async ValueTask<Result<AcceptedArchitectureBaseline>> Handle(
        AcceptArchitectureBaselineCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var verification = await verificationService.VerifyAsync(
            new VerifyArchitectureCommand(
                command.StartPath,
                command.ExplicitConfigurationPath,
                command.StateRootOverride),
            cancellationToken);
        if (!verification.IsSuccess)
        {
            return ResultFactory.Failure<AcceptedArchitectureBaseline>(verification.Problem!);
        }

        var workspace = workspaceLocator.Locate(command.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<AcceptedArchitectureBaseline>(workspace.Problem!);
        }

        var acceptedAt = timeProvider.GetUtcNow();
        var baseline = new ArchitectureVerificationBaseline(
            SchemaVersion: 1,
            RuleFingerprint: verification.Value.RuleFingerprint,
            AcceptedGraphRevision: verification.Value.GraphRevision,
            AcceptedAtUtc: acceptedAt,
            Findings: [.. verification.Value.Baseline.CurrentFindings
                .OrderBy(static finding => finding.Key, StringComparer.Ordinal)]);
        var write = await baselineRepository.WriteAsync(workspace.Value.RepositoryRoot, baseline, cancellationToken);
        if (!write.IsSuccess)
        {
            return ResultFactory.Failure<AcceptedArchitectureBaseline>(write.Problem!);
        }

        return ResultFactory.Success(new AcceptedArchitectureBaseline(
            write.Value,
            baseline.AcceptedGraphRevision,
            baseline.RuleFingerprint,
            baseline.Findings.Length,
            baseline.AcceptedAtUtc));
    }
}
