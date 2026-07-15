using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ProviderSiteMatches;

public static class ProviderSiteMatchContract
{
    public const string CurrentSchemaVersion = "provider-site-match/v1";

    public static Result<ProviderSiteMatch> Create(
        string providerId,
        string language,
        string? framework,
        string shape,
        ProviderSiteEvidence evidence,
        IReadOnlyList<ProviderSiteCapture> captures,
        ProviderSiteMatchState state,
        ProviderSiteMatchDiagnostic? diagnostic)
    {
        var problem = Validate(
            providerId,
            language,
            framework,
            shape,
            evidence,
            captures,
            state,
            diagnostic);
        return problem is null
            ? ResultFactory.Success(new ProviderSiteMatch(
                CurrentSchemaVersion,
                providerId,
                language,
                framework,
                shape,
                evidence,
                [.. captures.OrderBy(static capture => capture.Name, StringComparer.Ordinal)],
                state,
                diagnostic))
            : ResultFactory.Failure<ProviderSiteMatch>(problem);
    }

    public static Problem? Validate(ProviderSiteMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (!string.Equals(match.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            return Problem.Validation($"Provider site match schema '{match.SchemaVersion}' is not supported.");
        }

        return Validate(
            match.ProviderId,
            match.Language,
            match.Framework,
            match.Shape,
            match.Evidence,
            match.Captures,
            match.State,
            match.Diagnostic);
    }

    private static Problem? Validate(
        string providerId,
        string language,
        string? framework,
        string shape,
        ProviderSiteEvidence evidence,
        IReadOnlyList<ProviderSiteCapture> captures,
        ProviderSiteMatchState state,
        ProviderSiteMatchDiagnostic? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(providerId) ||
            string.IsNullOrWhiteSpace(language) ||
            string.IsNullOrWhiteSpace(shape) ||
            evidence is null ||
            captures is null ||
            string.IsNullOrWhiteSpace(evidence.RepositoryRelativePath) ||
            Path.IsPathRooted(evidence.RepositoryRelativePath) ||
            string.IsNullOrWhiteSpace(evidence.SourceContentHash) ||
            evidence.StartLine <= 0 ||
            evidence.StartColumn <= 0 ||
            evidence.EndLine < evidence.StartLine ||
            (evidence.EndLine == evidence.StartLine && evidence.EndColumn < evidence.StartColumn) ||
            evidence.EndColumn <= 0)
        {
            return Problem.Validation("Provider site matches require a provider, language, shape, repository-relative evidence, content hash, and valid source range.");
        }

        if (framework is not null && string.IsNullOrWhiteSpace(framework))
        {
            return Problem.Validation("Provider site match framework is either absent or a non-empty identifier.");
        }

        if (captures.Any(static capture => capture is null || string.IsNullOrWhiteSpace(capture.Name) || string.IsNullOrWhiteSpace(capture.RawValue)) ||
            captures.Select(static capture => capture.Name).Distinct(StringComparer.Ordinal).Count() != captures.Count)
        {
            return Problem.Validation("Provider site captures require unique non-empty names and raw values.");
        }

        if (state == ProviderSiteMatchState.Matched && diagnostic is not null)
        {
            return Problem.Validation("Matched provider sites cannot carry a diagnostic.");
        }

        if (state != ProviderSiteMatchState.Matched &&
            (diagnostic is null || string.IsNullOrWhiteSpace(diagnostic.Code) || string.IsNullOrWhiteSpace(diagnostic.Message)))
        {
            return Problem.Validation("Unresolved, unsupported, and degraded provider sites require an explicit diagnostic.");
        }

        return null;
    }
}
