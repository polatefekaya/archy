using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.InventorySources;

public sealed class RepositorySourceInventory(
    TimeProvider timeProvider,
    IWorkspaceLockManager lockManager)
    : IRepositorySourceInventory
{
    public async ValueTask<Result<SourceInventory>> SynchronizeAsync(
        WorkspaceStateLocation location,
        string repositoryRoot,
        ArchyConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!File.Exists(location.DatabasePath))
        {
            return ResultFactory.Failure<SourceInventory>(
                Problem.NotFound("The workspace has not been initialized. Run 'archy workspace init' before inventorying source files."));
        }

        var lease = await lockManager.AcquireAsync(
            location,
            WorkspaceLockMode.Write,
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!lease.IsSuccess)
        {
            return ResultFactory.Failure<SourceInventory>(lease.Problem!);
        }

        using var heldLease = lease.Value;
        var scope = await SourceScopePolicy.CreateAsync(
            repositoryRoot,
            location.StateDirectory,
            configuration.Scope,
            cancellationToken);
        if (!scope.IsSuccess)
        {
            return ResultFactory.Failure<SourceInventory>(scope.Problem!);
        }

        var scanned = await ScanAsync(repositoryRoot, scope.Value, cancellationToken);
        if (!scanned.IsComplete)
        {
            return ResultFactory.Success(new SourceInventory(
                IsComplete: false,
                scanned.Files,
                Changes: [],
                ParseCandidates: [],
                scanned.Exclusions,
                scanned.Diagnostics));
        }

        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            var cached = await ReadCachedFilesAsync(connection, location.WorkspaceId, cancellationToken);
            var changes = CalculateChanges(scanned.Files, cached);
            var parseCandidates = changes
                .Where(static change => change.Kind is SourceFileChangeKind.Added or SourceFileChangeKind.Changed or SourceFileChangeKind.Moved)
                .Select(static change => change.File)
                .Where(static file => file.Language == SourceLanguage.CSharp)
                .ToArray();
            await PersistCacheAsync(
                connection,
                location.WorkspaceId,
                scanned.Files,
                changes
                    .Where(static change => change.Kind == SourceFileChangeKind.Deleted)
                    .Select(static change => change.File)
                    .Concat(changes
                        .Where(static change => change.Kind == SourceFileChangeKind.Moved)
                        .Select(static change => change.PreviousFile)
                        .OfType<SourceFile>()),
                timeProvider.GetUtcNow(),
                cancellationToken);
            return ResultFactory.Success(new SourceInventory(
                IsComplete: true,
                scanned.Files,
                changes,
                parseCandidates,
                scanned.Exclusions,
                scanned.Diagnostics));
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            return ResultFactory.Failure<SourceInventory>(
                Problem.Storage($"Archy could not synchronize the source inventory cache: {exception.Message}"));
        }
    }

    private static async Task<ScannedRepository> ScanAsync(
        string repositoryRoot,
        SourceScopePolicy scope,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var directories = new Stack<string>();
        directories.Push(root);
        var files = new List<SourceFile>();
        var exclusions = new List<SourcePathExclusion>();
        var diagnostics = new List<SourceInventoryDiagnostic>();
        var isComplete = true;
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            string[] entries;
            try
            {
                entries = [.. Directory.EnumerateFileSystemEntries(directory).OrderBy(static path => path, StringComparer.Ordinal)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                isComplete = false;
                diagnostics.Add(new SourceInventoryDiagnostic(
                    ToDiagnosticPath(root, directory),
                    "directory_unavailable",
                    $"Archy could not enumerate this directory: {exception.Message}"));
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = SourceScopePolicy.NormalizeRepositoryRelativePath(Path.GetRelativePath(root, entry));
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    isComplete = false;
                    diagnostics.Add(new SourceInventoryDiagnostic(
                        relativePath,
                        "path_unavailable",
                        $"Archy could not inspect this path: {exception.Message}"));
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    exclusions.Add(new SourcePathExclusion(
                        relativePath,
                        SourcePathExclusionReason.SymbolicLink,
                        "symbolic-link"));
                    continue;
                }

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var decision = scope.Explain(relativePath, isDirectory);
                if (isDirectory)
                {
                    if (!decision.IsIncluded && decision.ExclusionReason is
                        SourcePathExclusionReason.GitMetadata or
                        SourcePathExclusionReason.ArchyState or
                        SourcePathExclusionReason.GeneratedDirectory or
                        SourcePathExclusionReason.ScopeExclude)
                    {
                        exclusions.Add(new SourcePathExclusion(relativePath, decision.ExclusionReason.Value, decision.MatchedRule!));
                        continue;
                    }

                    directories.Push(entry);
                    continue;
                }

                if (!decision.IsIncluded)
                {
                    exclusions.Add(new SourcePathExclusion(relativePath, decision.ExclusionReason!.Value, decision.MatchedRule!));
                    continue;
                }

                var hashed = await HashFileAsync(entry, relativePath, cancellationToken);
                if (!hashed.IsSuccess)
                {
                    isComplete = false;
                    diagnostics.Add(new SourceInventoryDiagnostic(relativePath, hashed.Problem!.Code, hashed.Problem.Message));
                    continue;
                }

                files.Add(hashed.Value);
            }
        }

        return new ScannedRepository(
            isComplete,
            [.. files.OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal)],
            [.. exclusions.OrderBy(static exclusion => exclusion.RepositoryRelativePath, StringComparer.Ordinal)],
            [.. diagnostics.OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)]);
    }

    private static async Task<Result<SourceFile>> HashFileAsync(
        string fullPath,
        string repositoryRelativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = new FileInfo(fullPath);
                before.Refresh();
                var beforeLength = before.Length;
                var beforeWriteTime = before.LastWriteTimeUtc;
                await using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 65_536,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken);
                var after = new FileInfo(fullPath);
                after.Refresh();
                if (beforeLength == after.Length && beforeWriteTime == after.LastWriteTimeUtc)
                {
                    return ResultFactory.Success(new SourceFile(
                        repositoryRelativePath,
                        ClassifyLanguage(repositoryRelativePath),
                        Convert.ToHexString(hash),
                        after.Length));
                }
            }

            return ResultFactory.Failure<SourceFile>(
                Problem.Storage("The file changed repeatedly while Archy was hashing it."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<SourceFile>(
                Problem.Storage($"Archy could not hash this file: {exception.Message}"));
        }
    }

    private static SourceLanguage ClassifyLanguage(string repositoryRelativePath) =>
        repositoryRelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ? SourceLanguage.CSharp
            : SourceLanguage.Unknown;

    private static IReadOnlyList<SourceFileChange> CalculateChanges(
        IReadOnlyList<SourceFile> files,
        IReadOnlyDictionary<string, SourceFile> cached)
    {
        var changes = new List<SourceFileChange>(files.Count + cached.Count);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            observed.Add(file.RepositoryRelativePath);
            if (!cached.TryGetValue(file.RepositoryRelativePath, out var prior))
            {
                changes.Add(new SourceFileChange(SourceFileChangeKind.Added, file));
            }
            else if (!Equals(file, prior))
            {
                changes.Add(new SourceFileChange(SourceFileChangeKind.Changed, file));
            }
            else
            {
                changes.Add(new SourceFileChange(SourceFileChangeKind.Unchanged, file));
            }
        }

        var deleted = cached.Values
            .Where(file => !observed.Contains(file.RepositoryRelativePath))
            .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal)
            .ToArray();
        var unreconciledDeleted = ReconcileMoves(changes, deleted);
        changes.AddRange(unreconciledDeleted.Select(static file => new SourceFileChange(SourceFileChangeKind.Deleted, file)));
        return [.. changes
            .OrderBy(static change => change.File.RepositoryRelativePath, StringComparer.Ordinal)
            .ThenBy(static change => change.Kind)];
    }

    private static IReadOnlyList<SourceFile> ReconcileMoves(List<SourceFileChange> changes, IReadOnlyList<SourceFile> deleted)
    {
        var addedByFingerprint = changes
            .Where(static change => change.Kind == SourceFileChangeKind.Added)
            .GroupBy(static change => FileFingerprint(change.File), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var deletedByFingerprint = deleted
            .GroupBy(FileFingerprint, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var consumedDeletedPaths = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            if (change.Kind != SourceFileChangeKind.Added ||
                !addedByFingerprint.TryGetValue(FileFingerprint(change.File), out var addedMatches) ||
                !deletedByFingerprint.TryGetValue(FileFingerprint(change.File), out var deletedMatches) ||
                addedMatches.Length != 1 ||
                deletedMatches.Length != 1)
            {
                continue;
            }

            var prior = deletedMatches[0];
            changes[index] = new SourceFileChange(SourceFileChangeKind.Moved, change.File, prior);
            consumedDeletedPaths.Add(prior.RepositoryRelativePath);
        }

        return [.. deleted.Where(file => !consumedDeletedPaths.Contains(file.RepositoryRelativePath))];
    }

    private static string FileFingerprint(SourceFile file) => string.Concat(
        file.Language.ToString(),
        "|",
        file.ByteLength.ToString(CultureInfo.InvariantCulture),
        "|",
        file.ContentHash);

    private static async Task<Dictionary<string, SourceFile>> ReadCachedFilesAsync(
        SqliteConnection connection,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT repository_relative_path, language, content_hash, byte_length FROM source_inventory_files WHERE workspace_id = $workspaceId;";
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var files = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var language = reader.GetString(1) switch
            {
                "csharp" => SourceLanguage.CSharp,
                "unknown" => SourceLanguage.Unknown,
                _ => throw new InvalidOperationException("The source inventory cache contains an unsupported language value."),
            };
            var file = new SourceFile(reader.GetString(0), language, reader.GetString(2), reader.GetInt64(3));
            files.Add(file.RepositoryRelativePath, file);
        }

        return files;
    }

    private static async Task PersistCacheAsync(
        SqliteConnection connection,
        string workspaceId,
        IReadOnlyList<SourceFile> files,
        IEnumerable<SourceFile> deletedFiles,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var file in files)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO source_inventory_files(workspace_id, repository_relative_path, language, content_hash, byte_length, observed_at_utc) VALUES ($workspaceId, $path, $language, $hash, $byteLength, $observedAt) ON CONFLICT(workspace_id, repository_relative_path) DO UPDATE SET language = excluded.language, content_hash = excluded.content_hash, byte_length = excluded.byte_length, observed_at_utc = excluded.observed_at_utc;";
            command.Parameters.AddWithValue("$workspaceId", workspaceId);
            command.Parameters.AddWithValue("$path", file.RepositoryRelativePath);
            command.Parameters.AddWithValue("$language", ToDatabase(file.Language));
            command.Parameters.AddWithValue("$hash", file.ContentHash);
            command.Parameters.AddWithValue("$byteLength", file.ByteLength);
            command.Parameters.AddWithValue("$observedAt", observedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var file in deletedFiles)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM source_inventory_files WHERE workspace_id = $workspaceId AND repository_relative_path = $path;";
            command.Parameters.AddWithValue("$workspaceId", workspaceId);
            command.Parameters.AddWithValue("$path", file.RepositoryRelativePath);
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string ToDatabase(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp => "csharp",
        SourceLanguage.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown source language."),
    };

    private static string ToDiagnosticPath(string repositoryRoot, string path)
    {
        var relative = Path.GetRelativePath(repositoryRoot, path);
        return relative == "." ? "." : SourceScopePolicy.NormalizeRepositoryRelativePath(relative);
    }

    private sealed record ScannedRepository(
        bool IsComplete,
        IReadOnlyList<SourceFile> Files,
        IReadOnlyList<SourcePathExclusion> Exclusions,
        IReadOnlyList<SourceInventoryDiagnostic> Diagnostics);
}
