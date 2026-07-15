using Archy.Features.Workspaces.ReadRepositoryCommit;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

/// <summary>Combines hook evidence with bounded snapshots and a Git delta; it never guesses shell writes from command text.</summary>
public sealed class PostToolChangedPathResolver(IRepositoryProvenanceReader provenanceReader) : IPostToolChangedPathResolver
{
    public async ValueTask<Result<PostToolChangedPathResolution>> ResolveAsync(
        PostToolChangedPathRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.ToolKind) || request.HookReportedPaths is null || request.PreToolDirtyPaths is null)
        {
            return ResultFactory.Failure<PostToolChangedPathResolution>(
                Problem.Validation("Post-tool changed-path resolution requires a recognized tool and explicit hook/Git inputs."));
        }

        var provenance = await provenanceReader.ReadAsync(request.Workspace, cancellationToken);
        if (!provenance.IsSuccess)
        {
            return ResultFactory.Failure<PostToolChangedPathResolution>(provenance.Problem!);
        }

        var before = ToEntries(request.BeforeSnapshot);
        var after = ToEntries(request.AfterSnapshot);
        var snapshotChanges = before.Keys
            .Union(after.Keys, StringComparer.Ordinal)
            .Where(path => !Equals(before.GetValueOrDefault(path), after.GetValueOrDefault(path)))
            .ToHashSet(StringComparer.Ordinal);
        var priorDirty = request.PreToolDirtyPaths
            .Select(NormalizePath)
            .Where(static path => path is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var gitDelta = provenance.Value.ChangedPaths
            .Select(NormalizePath)
            .Where(static path => path is not null)
            .Cast<string>()
            .Where(path => !priorDirty.Contains(path))
            .ToHashSet(StringComparer.Ordinal);
        var hookPaths = request.HookReportedPaths
            .Select(NormalizePath)
            .Where(static path => path is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var candidates = hookPaths
            .Union(snapshotChanges, StringComparer.Ordinal)
            .Union(gitDelta, StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var paths = new List<ResolvedPostToolPath>(candidates.Length);
        foreach (var path in candidates)
        {
            var evidence = new List<ChangedPathEvidence>(3);
            if (hookPaths.Contains(path))
            {
                evidence.Add(ChangedPathEvidence.HookInput);
            }

            if (snapshotChanges.Contains(path))
            {
                evidence.Add(ChangedPathEvidence.WorkspaceSnapshot);
            }

            if (gitDelta.Contains(path))
            {
                evidence.Add(ChangedPathEvidence.GitDelta);
            }

            paths.Add(new ResolvedPostToolPath(path, Categorize(path, request, after, evidence), evidence));
        }

        return ResultFactory.Success(new PostToolChangedPathResolution(request.ToolKind, paths));
    }

    private static ChangedPathCategory Categorize(
        string path,
        PostToolChangedPathRequest request,
        Dictionary<string, WorkspacePathSnapshotEntry> after,
        List<ChangedPathEvidence> evidence)
    {
        if (evidence.Count == 1 && evidence[0] == ChangedPathEvidence.HookInput)
        {
            return ChangedPathCategory.NoOp;
        }

        var scope = request.Scope.Explain(path, isDirectory: false);
        if (!scope.IsIncluded)
        {
            return scope.ExclusionReason == Archy.Features.Analysis.InventorySources.SourcePathExclusionReason.GeneratedDirectory || IsGenerated(path)
                ? ChangedPathCategory.Generated
                : ChangedPathCategory.OutOfScope;
        }

        if (IsGenerated(path))
        {
            return ChangedPathCategory.Generated;
        }

        if (after.TryGetValue(path, out var entry) && entry.IsBinary)
        {
            return ChangedPathCategory.Binary;
        }

        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ? ChangedPathCategory.Code
            : ChangedPathCategory.Other;
    }

    private static Dictionary<string, WorkspacePathSnapshotEntry> ToEntries(WorkspacePathSnapshot? snapshot) =>
        snapshot?.Entries
            .Select(entry => new { Path = NormalizePath(entry.RepositoryRelativePath), Entry = entry })
            .Where(static item => item.Path is not null)
            .ToDictionary(static item => item.Path!, static item => item.Entry, StringComparer.Ordinal)
        ?? new Dictionary<string, WorkspacePathSnapshotEntry>(StringComparer.Ordinal);

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return null;
        }

        var normalized = path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or "..")
            ? null
            : normalized;
    }

    private static bool IsGenerated(string path) =>
        path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
}
