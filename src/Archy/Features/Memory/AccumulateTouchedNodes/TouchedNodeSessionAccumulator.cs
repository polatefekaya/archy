using System.Text.Json;
using Archy.Features.Memory.DetermineImportantNodes;
using Archy.Features.Memory.SummaryBatches;

namespace Archy.Features.Memory.AccumulateTouchedNodes;

/// <summary>Thread-safe per-session co-touch accumulator. It contains no timer: watcher/session lifecycles own settle and end timing.</summary>
public sealed class TouchedNodeSessionAccumulator(TimeProvider timeProvider) : ITouchedNodeSessionAccumulator
{
    private readonly object gate = new();
    private readonly Dictionary<string, SessionTouches> sessions = new(StringComparer.Ordinal);

    public int Record(TouchedNodeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SessionId);
        ArgumentNullException.ThrowIfNull(record.Eligibility);
        ArgumentNullException.ThrowIfNull(record.ChangedPaths);

        var changedPaths = record.ChangedPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .ToHashSet(StringComparer.Ordinal);
        if (changedPaths.Count == 0)
        {
            return 0;
        }

        var matching = record.Eligibility.Included
            .Where(target => target.SourcePaths.Any(path => changedPaths.Contains(NormalizePath(path))))
            .OrderBy(static target => target.Kind)
            .ThenBy(static target => target.StableId, StringComparer.Ordinal)
            .ToArray();
        if (matching.Length == 0)
        {
            return 0;
        }

        lock (gate)
        {
            if (!sessions.TryGetValue(record.SessionId, out var session))
            {
                session = new SessionTouches(record.Eligibility.SourceGraphRevision, timeProvider.GetUtcNow());
                sessions.Add(record.SessionId, session);
            }
            else if (record.Eligibility.SourceGraphRevision < session.SourceGraphRevision)
            {
                throw new ArgumentException("Touched-node records for a session cannot move backwards across graph revisions.", nameof(record));
            }
            else if (record.Eligibility.SourceGraphRevision > session.SourceGraphRevision)
            {
                session.SourceGraphRevision = record.Eligibility.SourceGraphRevision;
            }

            var added = 0;
            foreach (var target in matching)
            {
                if (session.Targets.ContainsKey((target.Kind, target.StableId)))
                {
                    continue;
                }

                session.NextTouchOrdinal++;
                session.Targets.Add((target.Kind, target.StableId), new TouchedTarget(target, session.NextTouchOrdinal));
                added++;
            }

            session.LastTouchedAtUtc = timeProvider.GetUtcNow();
            return added;
        }
    }

    public TouchedNodeBatchCandidate? PrepareFlush(
        string sessionId,
        string summaryBatchId,
        string settleReason,
        string modelRequestMetadataJson,
        bool isSessionEnd = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summaryBatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settleReason);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelRequestMetadataJson);
        ValidateJson(modelRequestMetadataJson);

        lock (gate)
        {
            if (!sessions.TryGetValue(sessionId, out var session) || session.Targets.Count == 0)
            {
                return null;
            }

            if (session.Prepared is not null)
            {
                if (!string.Equals(session.Prepared.SummaryBatchId, summaryBatchId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The current touched-node batch must be acknowledged or retried with its original ID before preparing another batch.");
                }

                return session.Prepared.Candidate;
            }

            var ordered = session.Targets.Values
                .OrderBy(static target => target.TouchOrdinal)
                .ThenBy(static target => target.Target.Kind)
                .ThenBy(static target => target.Target.StableId, StringComparer.Ordinal)
                .ToArray();
            var allOrdinals = ordered.Select(static target => target.TouchOrdinal).ToArray();
            var members = ordered.Select(target => new SummaryBatchMemberFact(
                TargetKind: ToDatabaseKind(target.Target.Kind),
                TargetStableId: target.Target.StableId,
                TouchOrdinal: target.TouchOrdinal,
                CoTouchedMemberOrdinals: [.. allOrdinals.Where(ordinal => ordinal != target.TouchOrdinal)])).ToArray();
            var reason = isSessionEnd ? $"session-end:{settleReason}" : settleReason;
            var batch = new SummaryBatchFact(
                summaryBatchId,
                sessionId,
                reason,
                SummaryBatchRequestState.Pending,
                session.SourceGraphRevision,
                modelRequestMetadataJson,
                members);
            var candidate = new TouchedNodeBatchCandidate(batch, session.FirstTouchedAtUtc, session.LastTouchedAtUtc);
            session.Prepared = new PreparedBatch(summaryBatchId, candidate, [.. ordered.Select(static target => (target.Target.Kind, target.Target.StableId))]);
            return candidate;
        }
    }

    public void AcknowledgePersisted(string sessionId, string summaryBatchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summaryBatchId);
        lock (gate)
        {
            if (!sessions.TryGetValue(sessionId, out var session) || session.Prepared is null ||
                !string.Equals(session.Prepared.SummaryBatchId, summaryBatchId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("No matching prepared touched-node batch exists for acknowledgement.");
            }

            foreach (var target in session.Prepared.TargetKeys)
            {
                session.Targets.Remove(target);
            }

            session.Prepared = null;
            if (session.Targets.Count == 0)
            {
                sessions.Remove(sessionId);
            }
        }
    }

    private static string NormalizePath(string path) => path.Trim().Replace('\\', '/');

    private static string ToDatabaseKind(ImportantNodeTargetKind kind) => kind switch
    {
        ImportantNodeTargetKind.Directory => "directory",
        ImportantNodeTargetKind.Namespace => "namespace",
        ImportantNodeTargetKind.PublicType => "public_type",
        ImportantNodeTargetKind.PublicApi => "public_api",
        ImportantNodeTargetKind.Module => "module",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown important-node target kind."),
    };

    private static void ValidateJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Summary batch request metadata must be valid JSON.", nameof(value), exception);
        }
    }

    private sealed class SessionTouches(long sourceGraphRevision, DateTimeOffset touchedAtUtc)
    {
        public long SourceGraphRevision { get; set; } = sourceGraphRevision;
        public DateTimeOffset FirstTouchedAtUtc { get; } = touchedAtUtc;
        public DateTimeOffset LastTouchedAtUtc { get; set; } = touchedAtUtc;
        public int NextTouchOrdinal { get; set; }
        public Dictionary<(ImportantNodeTargetKind Kind, string StableId), TouchedTarget> Targets { get; } = new();
        public PreparedBatch? Prepared { get; set; }
    }

    private sealed record TouchedTarget(ImportantNodeTarget Target, int TouchOrdinal);
    private sealed record PreparedBatch(
        string SummaryBatchId,
        TouchedNodeBatchCandidate Candidate,
        IReadOnlyList<(ImportantNodeTargetKind Kind, string StableId)> TargetKeys);
}
