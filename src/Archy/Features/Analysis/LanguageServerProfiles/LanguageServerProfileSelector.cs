using System.IO.Enumeration;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Analysis.LanguageServerProfiles;

/// <summary>Activates a declarative LSP profile from its document extensions and repository markers.</summary>
public sealed class LanguageServerProfileSelector(IExecutablePathProbe executableProbe) : ILanguageServerProfileSelector
{
    public LanguageServerProfileSelection Resolve(
        string repositoryRoot,
        LanguageServerProfileConfiguration profile,
        bool hasMatchingDocuments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(profile);
        if (!hasMatchingDocuments)
        {
            return new LanguageServerProfileSelection(
                LanguageServerProfileSelectionState.NotApplicable,
                profile,
                null,
                [new LanguageServerProfileSelectionDiagnostic("language_profile_no_matching_documents", $"Language-server profile '{profile.Id}' has no matching source documents in this snapshot.")]);
        }

        if (profile.Markers.Length > 0 && !HasAnyMarker(repositoryRoot, profile.Markers))
        {
            return new LanguageServerProfileSelection(
                LanguageServerProfileSelectionState.NotApplicable,
                profile,
                null,
                [new LanguageServerProfileSelectionDiagnostic("language_profile_marker_not_found", $"Language-server profile '{profile.Id}' did not find any configured repository marker.")]);
        }

        var executable = executableProbe.Resolve(profile.Command, repositoryRoot);
        return executable is null
            ? new LanguageServerProfileSelection(
                LanguageServerProfileSelectionState.Unavailable,
                profile,
                null,
                [new LanguageServerProfileSelectionDiagnostic("configured_language_server_unavailable", $"Language-server profile '{profile.Id}' could not find command '{profile.Command}'.")])
            : new LanguageServerProfileSelection(
                LanguageServerProfileSelectionState.Selected,
                profile,
                executable,
                [new LanguageServerProfileSelectionDiagnostic("language_server_handshake_required", "Executable selection succeeded; protocol and capability compatibility are verified during the versioned LSP initialize handshake.")]);
    }

    private static bool HasAnyMarker(string repositoryRoot, IReadOnlyList<string> markers)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                if (relativePath.Split('/').Any(static segment => segment is ".git" or "bin" or "obj"))
                {
                    continue;
                }

                if (markers.Any(marker => Matches(marker, relativePath)))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool Matches(string marker, string repositoryRelativePath) =>
        marker.Contains('/', StringComparison.Ordinal)
            ? FileSystemName.MatchesSimpleExpression(marker, repositoryRelativePath, ignoreCase: OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            : FileSystemName.MatchesSimpleExpression(marker, Path.GetFileName(repositoryRelativePath), ignoreCase: OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
}
