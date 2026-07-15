using System.Security.Cryptography;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

public sealed class BoundedWorkspacePathSnapshotter : IBoundedWorkspacePathSnapshotter
{
    private const int MaximumPaths = 256;
    private const long MaximumBytesPerFile = 5 * 1024 * 1024;

    public async ValueTask<Result<WorkspacePathSnapshot>> CaptureAsync(
        string repositoryRoot,
        IReadOnlyList<string> repositoryRelativePaths,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(repositoryRelativePaths);
        if (repositoryRelativePaths.Count > MaximumPaths)
        {
            return ResultFactory.Failure<WorkspacePathSnapshot>(
                Problem.Validation($"Post-tool workspace snapshots are limited to {MaximumPaths} paths."));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var paths = repositoryRelativePaths
            .Select(path => ResolvePath(root, path))
            .ToArray();
        foreach (var path in paths)
        {
            if (!path.IsSuccess)
            {
                return ResultFactory.Failure<WorkspacePathSnapshot>(path.Problem!);
            }
        }

        var entries = new List<WorkspacePathSnapshotEntry>(paths.Length);
        foreach (var path in paths.OrderBy(static path => path.Value.RepositoryRelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captured = await CaptureEntryAsync(path.Value, cancellationToken);
            if (!captured.IsSuccess)
            {
                return ResultFactory.Failure<WorkspacePathSnapshot>(captured.Problem!);
            }

            entries.Add(captured.Value);
        }

        return ResultFactory.Success(new WorkspacePathSnapshot(entries));
    }

    private static Result<ResolvedPath> ResolvePath(string repositoryRoot, string repositoryRelativePath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRelativePath) || Path.IsPathRooted(repositoryRelativePath))
        {
            return ResultFactory.Failure<ResolvedPath>(Problem.Validation("Post-tool paths must be non-empty repository-relative paths."));
        }

        var normalized = repositoryRelativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
        {
            return ResultFactory.Failure<ResolvedPath>(Problem.Validation("Post-tool paths cannot escape the repository root."));
        }

        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, normalized));
        var rootPrefix = string.Concat(repositoryRoot, Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            return ResultFactory.Failure<ResolvedPath>(Problem.Validation("Post-tool paths cannot escape the repository root."));
        }

        return ResultFactory.Success(new ResolvedPath(normalized, fullPath));
    }

    private static async ValueTask<Result<WorkspacePathSnapshotEntry>> CaptureEntryAsync(
        ResolvedPath path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path.FullPath))
            {
                return ResultFactory.Success(new WorkspacePathSnapshotEntry(path.RepositoryRelativePath, Exists: false, IsBinary: false, ContentHash: null));
            }

            var fileInfo = new FileInfo(path.FullPath);
            if (fileInfo.Length > MaximumBytesPerFile)
            {
                return ResultFactory.Success(new WorkspacePathSnapshotEntry(
                    path.RepositoryRelativePath,
                    Exists: true,
                    IsBinary: true,
                    ContentHash: $"metadata:{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}"));
            }

            var bytes = await File.ReadAllBytesAsync(path.FullPath, cancellationToken);
            return ResultFactory.Success(new WorkspacePathSnapshotEntry(
                path.RepositoryRelativePath,
                Exists: true,
                IsBinary: bytes.AsSpan().IndexOf((byte)0) >= 0 || IsKnownBinaryExtension(path.RepositoryRelativePath),
                ContentHash: Convert.ToHexString(SHA256.HashData(bytes))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<WorkspacePathSnapshotEntry>(
                Problem.Storage($"Archy could not capture post-tool path '{path.RepositoryRelativePath}': {exception.Message}"));
        }
    }

    private static bool IsKnownBinaryExtension(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".dll" or ".exe" or ".dylib" or ".so" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".pdf" or ".zip" or ".gz";

    private sealed record ResolvedPath(string RepositoryRelativePath, string FullPath);
}
