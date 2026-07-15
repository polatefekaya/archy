namespace Archy.Features.Analysis.ProviderJoinKeys;

public sealed record ProviderJoinKey(
    string SchemaVersion,
    string CaptureName,
    ProviderJoinStrategy Strategy,
    string RawValue,
    string NormalizedValue,
    ProviderJoinKeyNormalization Normalization);

public sealed record ProviderJoinKeyNormalization(
    string NormalizerId,
    IReadOnlyList<ProviderJoinKeyNormalizationStep> Steps);

public sealed record ProviderJoinKeyNormalizationStep(
    string Kind,
    string Value);

public enum ProviderJoinStrategy
{
    TypeEquality,
    StringEquality,
    CanonicalPattern,
    ConfigurationPath,
}
