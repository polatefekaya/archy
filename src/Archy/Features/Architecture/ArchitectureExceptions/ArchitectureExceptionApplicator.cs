using Archy.Features.Architecture.VerificationBaselines;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>Fails closed for expired decisions and never lets an exception match more than one stable finding key.</summary>
public sealed class ArchitectureExceptionApplicator : IArchitectureExceptionApplicator
{
    public Result<ArchitectureExceptionApplication> Apply(
        ArchitectureBaselineComparison comparison,
        ArchitectureExceptionPolicy policy,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.SchemaVersion != 1 || policy.Exceptions is null || policy.Exceptions.Any(static exception => exception is null))
        {
            return ResultFactory.Failure<ArchitectureExceptionApplication>(
                Problem.Validation("Architecture exception application requires a valid exception policy."));
        }

        var selectedByFinding = policy.Exceptions
            .GroupBy(static exception => exception.FindingKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(static exception => exception.CreatedAtUtc)
                    .ThenByDescending(static exception => exception.ExceptionId, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);
        var introducedKeys = comparison.Findings
            .Where(static outcome => outcome.Status == ArchitectureFindingStatus.Introduced)
            .Select(static outcome => outcome.Finding.Key)
            .ToHashSet(StringComparer.Ordinal);
        var statuses = policy.Exceptions
            .OrderBy(static exception => exception.CreatedAtUtc)
            .ThenBy(static exception => exception.ExceptionId, StringComparer.Ordinal)
            .Select(exception => Status(exception, selectedByFinding, introducedKeys, nowUtc))
            .ToArray();
        var outcomes = comparison.Findings
            .Select(outcome => ApplyOutcome(outcome, selectedByFinding, nowUtc))
            .ToArray();
        return ResultFactory.Success(new ArchitectureExceptionApplication(
            comparison with { Findings = outcomes },
            statuses));
    }

    private static ArchitectureExceptionStatus Status(
        ArchitectureExceptionDecision architectureException,
        Dictionary<string, ArchitectureExceptionDecision> selectedByFinding,
        HashSet<string> introducedKeys,
        DateTimeOffset nowUtc)
    {
        var isSelected = selectedByFinding.TryGetValue(architectureException.FindingKey, out var selected) &&
                         string.Equals(selected.ExceptionId, architectureException.ExceptionId, StringComparison.Ordinal);
        if (!isSelected || !introducedKeys.Contains(architectureException.FindingKey))
        {
            return new ArchitectureExceptionStatus(architectureException, ArchitectureExceptionState.Unused, false);
        }

        if (architectureException.ExpiresAtUtc <= nowUtc)
        {
            return new ArchitectureExceptionStatus(architectureException, ArchitectureExceptionState.Expired, false);
        }

        return architectureException.ReviewAtUtc <= nowUtc
            ? new ArchitectureExceptionStatus(architectureException, ArchitectureExceptionState.ReviewDue, true)
            : new ArchitectureExceptionStatus(architectureException, ArchitectureExceptionState.Active, true);
    }

    private static ArchitectureFindingOutcome ApplyOutcome(
        ArchitectureFindingOutcome outcome,
        Dictionary<string, ArchitectureExceptionDecision> selectedByFinding,
        DateTimeOffset nowUtc)
    {
        if (outcome.Status != ArchitectureFindingStatus.Introduced ||
            !selectedByFinding.TryGetValue(outcome.Finding.Key, out var architectureException) ||
            architectureException.ExpiresAtUtc <= nowUtc)
        {
            return outcome;
        }

        return outcome with { Status = ArchitectureFindingStatus.Excepted };
    }
}
