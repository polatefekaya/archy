using System.Text.Json;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Duplicates.EmbeddingCache;

/// <summary>Append-only cache rows make reuse exact while retaining the revision that created each vector.</summary>
public sealed class EmbeddingCacheRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IEmbeddingCacheRepository
{
    public async ValueTask<Result<EmbeddingCacheStatistics>> ReadStatisticsAsync(WorkspaceStateLocation location, string modelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return ResultFactory.Failure<EmbeddingCacheStatistics>(Problem.Validation("Embedding cache statistics require a model."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<EmbeddingCacheStatistics>(lease.Problem!);
        using var held = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT vector_dimensions, COUNT(*), MAX(created_at_utc) FROM embedding_cache_entries WHERE workspace_id=$workspaceId AND model_id=$modelId GROUP BY vector_dimensions ORDER BY vector_dimensions;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId); command.Parameters.AddWithValue("$modelId", modelId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken); var dimensions = new Dictionary<int, int>(); var count = 0; DateTimeOffset? latest = null;
            while (await reader.ReadAsync(cancellationToken)) { var dimension = reader.GetInt32(0); var dimensionCount = reader.GetInt32(1); dimensions.Add(dimension, dimensionCount); count += dimensionCount; if (!reader.IsDBNull(2)) { var timestamp = DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture); if (latest is null || timestamp > latest) latest = timestamp; } }
            return ResultFactory.Success(new EmbeddingCacheStatistics(count, dimensions, latest));
        }
        catch (SqliteException exception) { return ResultFactory.Failure<EmbeddingCacheStatistics>(Problem.Storage($"Archy could not read embedding cache statistics: {exception.Message}")); }
    }

    public async ValueTask<Result<IReadOnlyList<EmbeddingCacheEntry>>> ListByModelAsync(WorkspaceStateLocation location, string modelId, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelId) || limit is < 1 or > 2048) return ResultFactory.Failure<IReadOnlyList<EmbeddingCacheEntry>>(Problem.Validation("Embedding cache listing requires a model and a limit from 1 through 2048."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<IReadOnlyList<EmbeddingCacheEntry>>(lease.Problem!);
        using var held = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT method_stable_id, content_hash, vector_json, created_graph_revision, created_at_utc FROM embedding_cache_entries WHERE workspace_id=$workspaceId AND model_id=$modelId ORDER BY created_at_utc DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$workspaceId", location.WorkspaceId); command.Parameters.AddWithValue("$modelId", modelId); command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken); var entries = new List<EmbeddingCacheEntry>();
            while (await reader.ReadAsync(cancellationToken)) { var vector = DeserializeVector(reader.GetString(2)); if (vector is null) continue; entries.Add(new(new(reader.GetString(0), modelId, reader.GetString(1)), vector, reader.GetInt64(3), DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture))); }
            return ResultFactory.Success<IReadOnlyList<EmbeddingCacheEntry>>(entries);
        }
        catch (SqliteException exception) { return ResultFactory.Failure<IReadOnlyList<EmbeddingCacheEntry>>(Problem.Storage($"Archy could not list embedding cache entries: {exception.Message}")); }
    }
    public async ValueTask<Result<EmbeddingCacheEntry?>> FindAsync(WorkspaceStateLocation location, EmbeddingCacheKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!IsValidKey(key))
        {
            return ResultFactory.Failure<EmbeddingCacheEntry?>(Problem.Validation("Embedding cache lookup requires a method, model, and content hash."));
        }

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<EmbeddingCacheEntry?>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            var entry = await ReadEntryAsync(connection, transaction: null, location.WorkspaceId, key, cancellationToken);
            return entry is null
                ? ResultFactory.Success<EmbeddingCacheEntry?>(null)
                : ResultFactory.Success<EmbeddingCacheEntry?>(entry);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<EmbeddingCacheEntry?>(Problem.Storage($"Archy could not read the embedding cache: {exception.Message}"));
        }
        catch (InvalidDataException exception)
        {
            return ResultFactory.Failure<EmbeddingCacheEntry?>(Problem.Storage(exception.Message));
        }
    }

    public async ValueTask<Result<EmbeddingCacheEntry>> StoreAsync(WorkspaceStateLocation location, EmbeddingCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsValidEntry(entry)) return ResultFactory.Failure<EmbeddingCacheEntry>(Problem.Validation("Embedding cache entries require a bounded finite vector and immutable graph provenance."));

        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Write, TimeSpan.FromSeconds(30), cancellationToken);
        if (!lease.IsSuccess) return ResultFactory.Failure<EmbeddingCacheEntry>(lease.Problem!);
        using var heldLease = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init();
            await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            if (!await RevisionBelongsToWorkspaceAsync(connection, transaction, location.WorkspaceId, entry.CreatedGraphRevision, cancellationToken))
            {
                return ResultFactory.Failure<EmbeddingCacheEntry>(Problem.Conflict("An embedding cache entry must reference a graph revision in the same workspace."));
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO embedding_cache_entries(workspace_id, method_stable_id, model_id, content_hash, vector_json, vector_dimensions, created_graph_revision, created_at_utc) VALUES ($workspaceId, $methodStableId, $modelId, $contentHash, $vectorJson, $dimensions, $createdGraphRevision, $createdAt) ON CONFLICT(workspace_id, method_stable_id, model_id, content_hash) DO NOTHING;";
            AddKey(insert, location.WorkspaceId, entry.Key);
            var createdAt = timeProvider.GetUtcNow();
            insert.Parameters.AddWithValue("$vectorJson", JsonSerializer.Serialize(entry.Vector.ToArray(), EmbeddingCacheJsonContext.Default.SingleArray));
            insert.Parameters.AddWithValue("$dimensions", entry.Vector.Count);
            insert.Parameters.AddWithValue("$createdGraphRevision", entry.CreatedGraphRevision);
            insert.Parameters.AddWithValue("$createdAt", createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync(cancellationToken);
            var stored = await ReadEntryAsync(connection, transaction, location.WorkspaceId, entry.Key, cancellationToken);
            if (stored is null)
            {
                return ResultFactory.Failure<EmbeddingCacheEntry>(Problem.Storage("Archy could not read the stored embedding cache entry."));
            }
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(stored);
        }
        catch (SqliteException exception)
        {
            return ResultFactory.Failure<EmbeddingCacheEntry>(Problem.Storage($"Archy could not persist the embedding cache: {exception.Message}"));
        }
        catch (InvalidDataException exception)
        {
            return ResultFactory.Failure<EmbeddingCacheEntry>(Problem.Storage(exception.Message));
        }
    }

    private static void AddKey(SqliteCommand command, string workspaceId, EmbeddingCacheKey key)
    {
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$methodStableId", key.MethodStableId);
        command.Parameters.AddWithValue("$modelId", key.ModelId);
        command.Parameters.AddWithValue("$contentHash", key.ContentHash);
    }

    private static async Task<bool> RevisionBelongsToWorkspaceAsync(SqliteConnection connection, SqliteTransaction transaction, string workspaceId, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM graph_revisions WHERE revision = $revision AND workspace_id = $workspaceId);";
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    private static bool IsValidKey(EmbeddingCacheKey? key) => key is not null && !string.IsNullOrWhiteSpace(key.MethodStableId) && !string.IsNullOrWhiteSpace(key.ModelId) &&
        key.ContentHash.Length is >= 16 and <= 256 && key.ContentHash.All(static character => char.IsAsciiHexDigit(character));

    private static bool IsValidEntry(EmbeddingCacheEntry entry) => IsValidKey(entry.Key) && entry.CreatedGraphRevision > 0 && entry.Vector is { Count: > 0 and <= 32_768 } &&
        entry.Vector.All(static value => float.IsFinite(value));

    private static async Task<EmbeddingCacheEntry?> ReadEntryAsync(SqliteConnection connection, SqliteTransaction? transaction, string workspaceId, EmbeddingCacheKey key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT vector_json, created_graph_revision, created_at_utc FROM embedding_cache_entries WHERE workspace_id = $workspaceId AND method_stable_id = $methodStableId AND model_id = $modelId AND content_hash = $contentHash;";
        AddKey(command, workspaceId, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var vector = DeserializeVector(reader.GetString(0));
        if (vector is null) throw new InvalidDataException("Archy found an invalid cached embedding vector.");
        return new EmbeddingCacheEntry(key, vector, reader.GetInt64(1), DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static float[]? DeserializeVector(string json)
    {
        try
        {
            var vector = JsonSerializer.Deserialize(json, EmbeddingCacheJsonContext.Default.SingleArray);
            return vector is { Length: > 0 and <= 32_768 } && vector.All(float.IsFinite) ? vector : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
