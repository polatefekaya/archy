using System.Text.Json;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Duplicates.EmbeddingCache;

/// <summary>Append-only cache rows make reuse exact while retaining the revision that created each vector.</summary>
public sealed class EmbeddingCacheRepository(TimeProvider timeProvider, IWorkspaceLockManager lockManager) : IEmbeddingCacheRepository
{
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
