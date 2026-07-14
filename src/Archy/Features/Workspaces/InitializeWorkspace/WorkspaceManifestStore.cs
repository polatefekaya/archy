using System.Text.Json;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed class WorkspaceManifestStore(TimeProvider timeProvider) : IWorkspaceManifestStore
{
    public async ValueTask<Result<WorkspaceManifestWriteResult>> LoadOrCreateAsync(
        WorkspaceStateLocation location,
        LocatedWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(workspace);

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(location.ManifestPath))
        {
            return await ReadExistingAsync(location, workspace, cancellationToken);
        }

        try
        {
            Directory.CreateDirectory(location.StateDirectory);
            var manifest = new WorkspaceManifest(
                FormatVersion: 1,
                location.WorkspaceId,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.RepositoryRoot)),
                workspace.GitMetadataPath,
                workspace.IsLinkedWorktree,
                timeProvider.GetUtcNow());
            var temporaryPath = Path.Combine(location.StateDirectory, $".{Guid.NewGuid():N}.workspace.json.tmp");

            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        manifest,
                        WorkspaceManifestJsonContext.Default.WorkspaceManifest,
                        cancellationToken);
                }

                File.Move(temporaryPath, location.ManifestPath, overwrite: false);
                return ResultFactory.Success(new WorkspaceManifestWriteResult(manifest, WasCreated: true));
            }
            catch (IOException) when (File.Exists(location.ManifestPath))
            {
                return await ReadExistingAsync(location, workspace, cancellationToken);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return ResultFactory.Failure<WorkspaceManifestWriteResult>(
                Problem.Storage($"Archy could not initialize local workspace state: {exception.Message}"));
        }
    }

    private static async ValueTask<Result<WorkspaceManifestWriteResult>> ReadExistingAsync(
        WorkspaceStateLocation location,
        LocatedWorkspace workspace,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                location.ManifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var manifest = await JsonSerializer.DeserializeAsync(
                stream,
                WorkspaceManifestJsonContext.Default.WorkspaceManifest,
                cancellationToken);

            if (manifest is null)
            {
                return ResultFactory.Failure<WorkspaceManifestWriteResult>(
                    Problem.Storage($"The workspace manifest '{location.ManifestPath}' is empty."));
            }

            var canonicalRepositoryRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(workspace.RepositoryRoot));
            if (!string.Equals(manifest.RepositoryRoot, canonicalRepositoryRoot, StringComparison.Ordinal))
            {
                return ResultFactory.Failure<WorkspaceManifestWriteResult>(
                    Problem.Conflict($"The existing state directory belongs to '{manifest.RepositoryRoot}'."));
            }

            return ResultFactory.Success(new WorkspaceManifestWriteResult(manifest, WasCreated: false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return ResultFactory.Failure<WorkspaceManifestWriteResult>(
                Problem.Storage($"Archy could not read local workspace state: {exception.Message}"));
        }
    }
}
