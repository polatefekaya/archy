using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Microsoft.Data.Sqlite;

namespace Archy.Features.Similarity.DetectReintroducedCapability;

public sealed record HistoricalCapabilityFingerprint(string StableId, string DeclarationKind, string CanonicalKey, string DisplayName, string? FilePath, string ContentHash, long IntroducedRevision, long RemovalRevision);
public sealed record ReintroducedCapabilityEvidence(string Kind, double Score, string Detail);
public sealed record ReintroducedCapabilityMatch(
    string CurrentStableId,
    string HistoricalStableId,
    long HistoricalRevision,
    long RemovalRevision,
    string? PreviousPath,
    double Score,
    IReadOnlyList<ReintroducedCapabilityEvidence> Evidence,
    IReadOnlyList<string> DecisionIds,
    IReadOnlyList<string> SessionIds);
public sealed record ReintroducedCapabilityResult(long GraphRevision, IReadOnlyList<ReintroducedCapabilityMatch> Matches, bool Abstained, string? AbstentionReason);

public interface IReintroducedCapabilityDetector
{
    ValueTask<Result<ReintroducedCapabilityResult>> FindAsync(WorkspaceStateLocation location, GraphRevisionSnapshot snapshot, string currentStableId, CancellationToken cancellationToken);
}

/// <summary>Compares active declarations with removed graph-version fingerprints without treating a rename as a duplicate.</summary>
public sealed class ReintroducedCapabilityDetector(IWorkspaceLockManager lockManager) : IReintroducedCapabilityDetector
{
    public async ValueTask<Result<ReintroducedCapabilityResult>> FindAsync(WorkspaceStateLocation location, GraphRevisionSnapshot snapshot, string currentStableId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location); ArgumentNullException.ThrowIfNull(snapshot); ArgumentException.ThrowIfNullOrWhiteSpace(currentStableId);
        var current = snapshot.Nodes.SingleOrDefault(node => node.StableId == currentStableId);
        if (current is null) return ResultFactory.Failure<ReintroducedCapabilityResult>(Problem.NotFound("The current stable ID does not exist in the active graph revision."));
        var lease = await lockManager.AcquireAsync(location, WorkspaceLockMode.Read, TimeSpan.FromSeconds(30), cancellationToken); if (!lease.IsSuccess) return ResultFactory.Failure<ReintroducedCapabilityResult>(lease.Problem!); using var held = lease.Value;
        try
        {
            SQLitePCL.Batteries_V2.Init(); await using var connection = new SqliteConnection($"Data Source={location.DatabasePath}"); await connection.OpenAsync(cancellationToken);
            var historical = await ReadRemovedAsync(connection, location.WorkspaceId, snapshot.Revision, cancellationToken);
            if (historical.Count == 0) return ResultFactory.Success(new ReintroducedCapabilityResult(snapshot.Revision, [], true, "No removed historical capability fingerprints are available."));
            var provenance = await ReadProvenanceAsync(connection, location.WorkspaceId, historical.Select(static fingerprint => fingerprint.StableId), cancellationToken);
            var matches = historical.Select(old => Match(current, old, provenance.TryGetValue(old.StableId, out var evidence) ? evidence : HistoricalProvenance.Empty)).Where(match => match is not null).Cast<ReintroducedCapabilityMatch>().OrderByDescending(match => match.Score).ThenByDescending(match => match.RemovalRevision).ThenBy(match => match.HistoricalStableId, StringComparer.Ordinal).ToArray();
            return ResultFactory.Success(new ReintroducedCapabilityResult(snapshot.Revision, matches, matches.Length == 0, matches.Length == 0 ? "No historical fingerprint met the independent-evidence threshold." : null));
        }
        catch (SqliteException exception) { return ResultFactory.Failure<ReintroducedCapabilityResult>(Problem.Storage($"Archy could not read historical capability fingerprints: {exception.Message}")); }
    }

    private static ReintroducedCapabilityMatch? Match(Graph.CommitGraphRevision.GraphNodeFact current, HistoricalCapabilityFingerprint old, HistoricalProvenance provenance)
    {
        var evidence = new List<ReintroducedCapabilityEvidence>();
        if (current.StableId == old.StableId) evidence.Add(new("stable_identity", 1d, "The stable semantic identity was previously removed and is active again."));
        var name = Jaccard(Tokens(current.DisplayName + " " + current.CanonicalKey), Tokens(old.DisplayName + " " + old.CanonicalKey)); if (name > 0d) evidence.Add(new("name_signature", name, "Declaration name and canonical-key tokens overlap."));
        var path = Jaccard(Tokens(current.FilePath), Tokens(old.FilePath)); if (path > 0d) evidence.Add(new("path_context", path, "Historical and current source-path tokens overlap."));
        if (current.ContentHash == old.ContentHash) evidence.Add(new("content_hash", 1d, "The persisted declaration content hash matches."));
        var independent = evidence.Select(item => item.Kind).Distinct(StringComparer.Ordinal).Count(); var score = Math.Round(evidence.Average(item => item.Score), 6);
        return independent >= 2 && score >= .60d ? new(current.StableId, old.StableId, old.IntroducedRevision, old.RemovalRevision, old.FilePath, score, evidence, provenance.DecisionIds, provenance.SessionIds) : null;
    }

    private static async Task<IReadOnlyList<HistoricalCapabilityFingerprint>> ReadRemovedAsync(SqliteConnection connection, string workspaceId, long revision, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT stable_id,node_kind,canonical_key,display_name,file_path,content_hash,valid_from_revision,valid_to_revision FROM graph_node_versions WHERE workspace_id=$workspace AND valid_to_revision IS NOT NULL AND valid_to_revision < $revision ORDER BY valid_to_revision DESC,stable_id LIMIT 500"; command.Parameters.AddWithValue("$workspace", workspaceId); command.Parameters.AddWithValue("$revision", revision); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<HistoricalCapabilityFingerprint>(); while (await reader.ReadAsync(cancellationToken)) results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7))); return results;
    }
    private static async Task<IReadOnlyDictionary<string, HistoricalProvenance>> ReadProvenanceAsync(SqliteConnection connection, string workspaceId, IEnumerable<string> stableIds, CancellationToken cancellationToken)
    {
        var ids = stableIds.Distinct(StringComparer.Ordinal).Take(500).ToArray();
        if (ids.Length == 0) return new Dictionary<string, HistoricalProvenance>(StringComparer.Ordinal);
        var placeholders = string.Join(',', ids.Select((_, index) => $"$id{index}"));
        var decisions = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal); var sessions = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT t.target_stable_id,d.decision_id,d.session_id FROM decisions d JOIN decision_targets t ON t.decision_id=d.decision_id WHERE d.workspace_id=$workspace AND t.target_stable_id IN ({placeholders}) ORDER BY d.occurred_at_utc DESC,d.decision_id;";
            command.Parameters.AddWithValue("$workspace", workspaceId); for (var index = 0; index < ids.Length; index++) command.Parameters.AddWithValue($"$id{index}", ids[index]);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) { var id = reader.GetString(0); Add(decisions, id, reader.GetString(1)); if (!reader.IsDBNull(2)) Add(sessions, id, reader.GetString(2)); }
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT target_stable_id,session_id FROM session_events WHERE workspace_id=$workspace AND target_stable_id IN ({placeholders}) ORDER BY occurred_at_utc DESC,sequence_number DESC;";
            command.Parameters.AddWithValue("$workspace", workspaceId); for (var index = 0; index < ids.Length; index++) command.Parameters.AddWithValue($"$id{index}", ids[index]);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) Add(sessions, reader.GetString(0), reader.GetString(1));
        }
        return ids.ToDictionary(id => id, id => new HistoricalProvenance(decisions.TryGetValue(id, out var decisionIds) ? decisionIds.Take(20).ToArray() : [], sessions.TryGetValue(id, out var sessionIds) ? sessionIds.Take(20).ToArray() : []), StringComparer.Ordinal);
    }
    private static void Add(Dictionary<string, SortedSet<string>> values, string key, string value) { if (!values.TryGetValue(key, out var set)) { set = new(StringComparer.Ordinal); values.Add(key, set); } set.Add(value); }
    private sealed record HistoricalProvenance(IReadOnlyList<string> DecisionIds, IReadOnlyList<string> SessionIds) { public static HistoricalProvenance Empty { get; } = new([], []); }
    private static HashSet<string> Tokens(string? value) => (value ?? string.Empty).Split(['.', ':', '/', '\\', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries).SelectMany(SplitCamel).Select(token => token.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
    private static IEnumerable<string> SplitCamel(string value) { var start = 0; for (var index = 1; index < value.Length; index++) if (char.IsUpper(value[index]) && char.IsLower(value[index - 1])) { yield return value[start..index]; start = index; } if (start < value.Length) yield return value[start..]; }
    private static double Jaccard(HashSet<string> left, HashSet<string> right) { var union = left.Union(right).Count(); return union == 0 ? 0d : left.Intersect(right).Count() / (double)union; }
}
