namespace Archy.Features.Analysis.ProviderSiteMatches;

public sealed record ProviderSiteMatch(
    string SchemaVersion,
    string ProviderId,
    string Language,
    string? Framework,
    string Shape,
    ProviderSiteEvidence Evidence,
    IReadOnlyList<ProviderSiteCapture> Captures,
    ProviderSiteMatchState State,
    ProviderSiteMatchDiagnostic? Diagnostic);

public sealed record ProviderSiteEvidence(
    string RepositoryRelativePath,
    string SourceContentHash,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public sealed record ProviderSiteCapture(
    string Name,
    ProviderSiteCaptureKind Kind,
    string RawValue);

public sealed record ProviderSiteMatchDiagnostic(
    string Code,
    string Message);

public enum ProviderSiteCaptureKind
{
    Type,
    Literal,
    Identifier,
    Attribute,
    Method,
    Metadata,
}

public enum ProviderSiteMatchState
{
    Matched,
    Unresolved,
    Unsupported,
    Degraded,
}
