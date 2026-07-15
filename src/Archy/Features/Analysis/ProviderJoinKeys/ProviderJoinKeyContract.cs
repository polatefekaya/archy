using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ProviderJoinKeys;

public static class ProviderJoinKeyContract
{
    public const string CurrentSchemaVersion = "provider-join-key/v1";

    public static Result<ProviderJoinKey> Create(
        string captureName,
        ProviderJoinStrategy strategy,
        string rawValue,
        string normalizedValue,
        ProviderJoinKeyNormalization normalization)
    {
        var problem = Validate(captureName, strategy, rawValue, normalizedValue, normalization);
        return problem is null
            ? ResultFactory.Success(new ProviderJoinKey(
                CurrentSchemaVersion,
                captureName,
                strategy,
                rawValue,
                normalizedValue,
                new ProviderJoinKeyNormalization(
                    normalization.NormalizerId,
                    [.. normalization.Steps
                        .OrderBy(static step => step.Kind, StringComparer.Ordinal)
                        .ThenBy(static step => step.Value, StringComparer.Ordinal)])))
            : ResultFactory.Failure<ProviderJoinKey>(problem);
    }

    public static Problem? Validate(ProviderJoinKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!string.Equals(key.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            return Problem.Validation($"Provider join-key schema '{key.SchemaVersion}' is not supported.");
        }

        return Validate(
            key.CaptureName,
            key.Strategy,
            key.RawValue,
            key.NormalizedValue,
            key.Normalization);
    }

    private static Problem? Validate(
        string captureName,
        ProviderJoinStrategy strategy,
        string rawValue,
        string normalizedValue,
        ProviderJoinKeyNormalization normalization)
    {
        if (string.IsNullOrWhiteSpace(captureName) ||
            !Enum.IsDefined(strategy) ||
            string.IsNullOrWhiteSpace(normalizedValue) ||
            normalization is null ||
            string.IsNullOrWhiteSpace(normalization.NormalizerId) ||
            normalization.Steps is null ||
            normalization.Steps.Any(static step => step is null || string.IsNullOrWhiteSpace(step.Kind)))
        {
            return Problem.Validation("Provider join keys require a capture name, normalized value, normalizer identifier, and valid normalization steps.");
        }

        if (normalization.Steps
            .Select(static step => string.Concat(step.Kind, "\u001f", step.Value))
            .Distinct(StringComparer.Ordinal)
            .Count() != normalization.Steps.Count)
        {
            return Problem.Validation("Provider join-key normalization steps must be unique.");
        }

        return strategy switch
        {
            ProviderJoinStrategy.TypeEquality when !normalization.NormalizerId.StartsWith("csharp-symbol", StringComparison.Ordinal) =>
                Problem.Validation("Type-equality join keys require a csharp-symbol normalizer."),
            ProviderJoinStrategy.CanonicalPattern when !normalization.Steps.Any(static step => step.Kind == "placeholder") =>
                Problem.Validation("Canonical-pattern join keys require placeholder normalization metadata."),
            ProviderJoinStrategy.ConfigurationPath when !normalization.Steps.Any(static step => step.Kind == "separator") =>
                Problem.Validation("Configuration-path join keys require separator normalization metadata."),
            _ => null,
        };
    }
}
