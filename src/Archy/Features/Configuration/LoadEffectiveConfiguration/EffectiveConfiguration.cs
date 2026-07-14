namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed record EffectiveConfiguration(
    ArchyConfiguration Configuration,
    string? StateRoot,
    ConfigurationSource[] Sources);

public sealed record ConfigurationSource(
    ConfigurationSourceKind Kind,
    string? Path,
    bool WasApplied);

public enum ConfigurationSourceKind
{
    Defaults,
    User,
    Repository,
    Explicit,
    CommandLine,
}
