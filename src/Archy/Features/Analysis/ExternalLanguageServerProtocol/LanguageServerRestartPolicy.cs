namespace Archy.Features.Analysis.ExternalLanguageServerProtocol;

public static class LanguageServerRestartPolicyEvaluator
{
    public static LanguageServerRestartDecision Decide(
        LanguageServerLaunchSpecification specification,
        int completedRestarts,
        LanguageServerFailureKind failure)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentOutOfRangeException.ThrowIfNegative(completedRestarts);

        if (completedRestarts >= specification.RestartPolicy.MaximumRestarts)
        {
            return new LanguageServerRestartDecision(false, completedRestarts, 0, $"{failure} exhausted the configured restart budget.");
        }

        var delay = checked(specification.RestartPolicy.InitialBackoffMilliseconds * (1 << completedRestarts));
        return new LanguageServerRestartDecision(true, completedRestarts + 1, delay, $"Restart after {failure}.");
    }
}
