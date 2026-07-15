using Archy.Features.Architecture.VerifyArchitecture;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>Prevents exception creation unless it names one exact currently introduced finding.</summary>
public sealed class AcceptArchitectureExceptionHandler(
    IArchitectureVerificationService verificationService,
    IWorkspaceLocator workspaceLocator,
    IArchitectureExceptionPolicyRepository exceptionRepository,
    TimeProvider timeProvider)
    : IRequestHandler<AcceptArchitectureExceptionCommand, Result<AcceptedArchitectureExceptionDecision>>
{
    public async ValueTask<Result<AcceptedArchitectureExceptionDecision>> Handle(
        AcceptArchitectureExceptionCommand command,
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
            return ResultFactory.Failure<AcceptedArchitectureExceptionDecision>(verification.Problem!);
        }

        if (!verification.Value.Baseline.Findings.Any(outcome =>
                outcome.Status == ArchitectureFindingStatus.Introduced &&
                string.Equals(outcome.Finding.Key, command.FindingKey, StringComparison.Ordinal)))
        {
            return ResultFactory.Failure<AcceptedArchitectureExceptionDecision>(
                Problem.Conflict("Architecture exceptions may target only one currently introduced deterministic finding by its exact key."));
        }

        var workspace = workspaceLocator.Locate(command.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<AcceptedArchitectureExceptionDecision>(workspace.Problem!);
        }

        var current = await exceptionRepository.ReadAsync(workspace.Value.RepositoryRoot, cancellationToken);
        if (!current.IsSuccess)
        {
            return ResultFactory.Failure<AcceptedArchitectureExceptionDecision>(current.Problem!);
        }

        var now = timeProvider.GetUtcNow();
        if (current.Value.Policy.Exceptions.Any(exception =>
                string.Equals(exception.FindingKey, command.FindingKey, StringComparison.Ordinal) &&
                exception.ExpiresAtUtc > now))
        {
            return ResultFactory.Failure<AcceptedArchitectureExceptionDecision>(
                Problem.Conflict("The finding already has an active architecture exception. Let it expire or remove the underlying violation instead of broadening suppression."));
        }

        var architectureException = new ArchitectureExceptionDecision(
            Guid.NewGuid().ToString("N"),
            command.FindingKey,
            command.Author,
            command.Reason,
            command.ReviewAtUtc,
            command.ExpiresAtUtc,
            now);
        var appended = await exceptionRepository.AppendAsync(
            workspace.Value.RepositoryRoot,
            architectureException,
            cancellationToken);
        if (!appended.IsSuccess)
        {
            return ResultFactory.Failure<AcceptedArchitectureExceptionDecision>(appended.Problem!);
        }

        return ResultFactory.Success(new AcceptedArchitectureExceptionDecision(
            appended.Value,
            architectureException.ExceptionId,
            architectureException.FindingKey,
            architectureException.ReviewAtUtc,
            architectureException.ExpiresAtUtc));
    }
}
