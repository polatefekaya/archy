using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Analysis.LanguageServerProfiles;

public sealed record LanguageServerProfileSelection(
    LanguageServerProfileSelectionState State,
    LanguageServerProfileConfiguration Profile,
    string? ExecutablePath,
    IReadOnlyList<LanguageServerProfileSelectionDiagnostic> Diagnostics);

public sealed record LanguageServerProfileSelectionDiagnostic(string Code, string Message);

public enum LanguageServerProfileSelectionState
{
    Selected,
    NotApplicable,
    Unavailable,
}
