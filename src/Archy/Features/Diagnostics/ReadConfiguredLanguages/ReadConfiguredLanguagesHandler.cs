using System.IO.Enumeration;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.LanguageServerProfiles;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Diagnostics.ReadConfiguredLanguages;

/// <summary>Non-mutating language readiness inventory; no analysis, workspace initialization, or LSP launch occurs here.</summary>
public sealed class ReadConfiguredLanguagesHandler(
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader,
    IWorkspaceStateLayout stateLayout,
    IExecutablePathProbe executableProbe)
    : IRequestHandler<ReadConfiguredLanguagesQuery, Result<IReadOnlyList<ConfiguredLanguageProfileStatus>>>
{
    private const int MaximumEnumeratedFiles = 20_000;

    public async ValueTask<Result<IReadOnlyList<ConfiguredLanguageProfileStatus>>> Handle(
        ReadConfiguredLanguagesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var workspace = workspaceLocator.Locate(query.Path);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ConfiguredLanguageProfileStatus>>(workspace.Problem!);
        }

        var effective = await configurationLoader.LoadAsync(
            workspace.Value,
            query.ConfigurationPath,
            query.StateRoot,
            cancellationToken);
        if (!effective.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ConfiguredLanguageProfileStatus>>(effective.Problem!);
        }

        var location = stateLayout.Resolve(workspace.Value, effective.Value.StateRoot);
        if (!location.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ConfiguredLanguageProfileStatus>>(location.Problem!);
        }

        var scope = await SourceScopePolicy.CreateAsync(
            workspace.Value.RepositoryRoot,
            location.Value.StateDirectory,
            effective.Value.Configuration.Scope,
            cancellationToken);
        if (!scope.IsSuccess)
        {
            return ResultFactory.Failure<IReadOnlyList<ConfiguredLanguageProfileStatus>>(scope.Problem!);
        }

        var files = EnumerateFiles(workspace.Value.RepositoryRoot, scope.Value, cancellationToken);
        var statuses = effective.Value.Configuration.LanguageServerProfiles
            .Select(profile => Read(workspace.Value.RepositoryRoot, profile, files))
            .OrderBy(static status => status.Id, StringComparer.Ordinal)
            .ToArray();
        return ResultFactory.Success<IReadOnlyList<ConfiguredLanguageProfileStatus>>(statuses);
    }

    private ConfiguredLanguageProfileStatus Read(
        string repositoryRoot,
        LanguageServerProfileConfiguration profile,
        RepositoryFileInventory files)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) ||
            string.IsNullOrWhiteSpace(profile.LanguageId) ||
            string.IsNullOrWhiteSpace(profile.Command) ||
            profile.Extensions.Length == 0 ||
            profile.Extensions.Any(static extension => string.IsNullOrWhiteSpace(extension) || extension[0] != '.'))
        {
            return new ConfiguredLanguageProfileStatus(
                profile.Id,
                profile.LanguageId,
                profile.Extensions,
                profile.Markers,
                profile.Command,
                profile.Arguments,
                profile.MaxSymbolQueries,
                CommandAvailable: false,
                MatchingSourceFileCount: 0,
                MatchedMarkers: [],
                LanguageProfileReadiness.InvalidConfiguration,
                "The configured language profile is incomplete or has invalid extensions.",
                "Correct the language-server profile configuration.");
        }

        var matchingSourceCount = files.InScopePaths.Count(path =>
            profile.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        var matchedMarkers = profile.Markers
            .Where(marker => files.MarkerCandidatePaths.Any(path => Matches(marker, path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var commandAvailable = executableProbe.Resolve(profile.Command, repositoryRoot) is not null;

        if (matchingSourceCount == 0)
        {
            return Status(
                profile,
                commandAvailable,
                matchingSourceCount,
                matchedMarkers,
                LanguageProfileReadiness.ConfiguredNoSources,
                "The profile is configured but has no matching in-scope source files.",
                remediation: null);
        }

        if (profile.Markers.Length > 0 && matchedMarkers.Length == 0)
        {
            return Status(
                profile,
                commandAvailable,
                matchingSourceCount,
                matchedMarkers,
                LanguageProfileReadiness.NoRepositoryMarker,
                "No configured repository marker is present.",
                "Add a repository marker or adjust the profile markers.");
        }

        if (!commandAvailable)
        {
            return Status(
                profile,
                commandAvailable,
                matchingSourceCount,
                matchedMarkers,
                LanguageProfileReadiness.CommandUnavailable,
                "Matching source files exist but the language-server command is unavailable.",
                $"Install or configure '{profile.Command}'.");
        }

        return Status(
            profile,
            commandAvailable,
            matchingSourceCount,
            matchedMarkers,
            LanguageProfileReadiness.Ready,
            "Configured sources, marker, and executable are available.",
            remediation: null);
    }

    private static ConfiguredLanguageProfileStatus Status(
        LanguageServerProfileConfiguration profile,
        bool commandAvailable,
        int matchingSourceCount,
        IReadOnlyList<string> matchedMarkers,
        LanguageProfileReadiness readiness,
        string detail,
        string? remediation) =>
        new(
            profile.Id,
            profile.LanguageId,
            profile.Extensions,
            profile.Markers,
            profile.Command,
            profile.Arguments,
            profile.MaxSymbolQueries,
            commandAvailable,
            matchingSourceCount,
            matchedMarkers,
            readiness,
            detail,
            remediation);

    private static RepositoryFileInventory EnumerateFiles(
        string repositoryRoot,
        SourceScopePolicy scope,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var directories = new Stack<string>();
        var inScopePaths = new List<string>();
        var markerCandidatePaths = new List<string>();
        directories.Push(root);
        while (directories.Count > 0 && markerCandidatePaths.Count < MaximumEnumeratedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            string[] entries;
            try
            {
                entries = [.. Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relativePath = SourceScopePolicy.NormalizeRepositoryRelativePath(Path.GetRelativePath(root, entry));
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var decision = scope.Explain(relativePath, isDirectory);
                if (isDirectory)
                {
                    if (!decision.IsIncluded && decision.ExclusionReason is
                        SourcePathExclusionReason.GitMetadata or
                        SourcePathExclusionReason.ArchyState or
                        SourcePathExclusionReason.GeneratedDirectory or
                        SourcePathExclusionReason.GitIgnore or
                        SourcePathExclusionReason.ScopeExclude)
                    {
                        continue;
                    }

                    directories.Push(entry);
                    continue;
                }

                markerCandidatePaths.Add(relativePath);
                if (decision.IsIncluded)
                {
                    inScopePaths.Add(relativePath);
                }

                if (markerCandidatePaths.Count >= MaximumEnumeratedFiles)
                {
                    break;
                }
            }
        }

        return new RepositoryFileInventory(inScopePaths, markerCandidatePaths);
    }

    private static bool Matches(string marker, string repositoryRelativePath) =>
        marker.Contains('/', StringComparison.Ordinal)
            ? FileSystemName.MatchesSimpleExpression(marker, repositoryRelativePath, ignoreCase: true)
            : FileSystemName.MatchesSimpleExpression(marker, Path.GetFileName(repositoryRelativePath), ignoreCase: true);

    private sealed record RepositoryFileInventory(
        IReadOnlyList<string> InScopePaths,
        IReadOnlyList<string> MarkerCandidatePaths);
}
