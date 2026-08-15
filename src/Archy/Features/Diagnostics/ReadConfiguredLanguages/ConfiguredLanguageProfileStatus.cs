namespace Archy.Features.Diagnostics.ReadConfiguredLanguages;

public enum LanguageProfileReadiness { Ready, ConfiguredNoSources, CommandUnavailable, NoRepositoryMarker, InvalidConfiguration, Unknown }
public sealed record ConfiguredLanguageProfileStatus(string Id, string LanguageId, IReadOnlyList<string> Extensions, IReadOnlyList<string> Markers, string Command, IReadOnlyList<string> Arguments, int MaxSymbolQueries, bool CommandAvailable, int MatchingSourceFileCount, IReadOnlyList<string> MatchedMarkers, LanguageProfileReadiness Readiness, string Detail, string? Remediation);
