namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

public sealed record LanguageServerTransportDiagnostic(string Code, string Message);

public sealed record LanguageServerStdioSessionDiagnostics(
    IReadOnlyList<LanguageServerTransportDiagnostic> Events,
    string CapturedStandardError,
    bool StandardErrorWasTruncated);
