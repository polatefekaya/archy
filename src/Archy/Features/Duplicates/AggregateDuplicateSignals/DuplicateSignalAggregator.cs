using System.Text.Json;
using Archy.Features.Duplicates.DuplicateFindings;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.AggregateDuplicateSignals;

/// <summary>Creates a durable duplicate finding only from corroborated, independently-derived evidence.</summary>
public sealed class DuplicateSignalAggregator(DuplicateSignalThresholds thresholds) : IDuplicateSignalAggregator
{
    public const string AggregationVersion = "duplicate-aggregate/v1";

    public DuplicateAggregationResult Aggregate(DuplicateSignalSet signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        Validate(signals, thresholds);
        var (left, right) = OrderTargets(signals.LeftTarget, signals.RightTarget);
        var qualified = signals.Signals
            .Where(signal => signal.Score >= thresholds.For(signal.Kind))
            .Select(static signal => signal.Kind)
            .Order()
            .ToArray();
        var isLikelyDuplicate = qualified.Length >= 2;
        var rationale = Rationale(qualified, signals.Signals, isLikelyDuplicate);
        if (!isLikelyDuplicate)
        {
            return new DuplicateAggregationResult(false, qualified, rationale, null);
        }

        var confidence = Math.Round(signals.Signals.Where(signal => qualified.Contains(signal.Kind)).Average(static signal => signal.Score), 6, MidpointRounding.AwayFromZero);
        var finding = new DuplicateFindingObservationFact(left, right, signals.GraphRevision, AggregationVersion, confidence, rationale, [.. signals.Signals.OrderBy(static signal => signal.Kind)]);
        return new DuplicateAggregationResult(true, qualified, rationale, finding);
    }

    private static void Validate(DuplicateSignalSet signals, DuplicateSignalThresholds thresholds)
    {
        if (signals.LeftTarget is null || signals.RightTarget is null || signals.LeftTarget.Kind != ArchitectureTargetKind.GraphNode || signals.RightTarget.Kind != ArchitectureTargetKind.GraphNode ||
            string.IsNullOrWhiteSpace(signals.LeftTarget.StableId) || string.IsNullOrWhiteSpace(signals.RightTarget.StableId) || string.Equals(signals.LeftTarget.StableId, signals.RightTarget.StableId, StringComparison.Ordinal) ||
            signals.GraphRevision < 1 || signals.Signals is null || signals.Signals.Count is < 1 or > 3 ||
            signals.Signals.Any(static signal => signal is null || !Enum.IsDefined(signal.Kind) || double.IsNaN(signal.Score) || double.IsInfinity(signal.Score) || signal.Score is < 0d or > 1d || string.IsNullOrWhiteSpace(signal.EvidenceJson) || !IsJson(signal.EvidenceJson)) ||
            signals.Signals.Select(static signal => signal.Kind).Distinct().Count() != signals.Signals.Count ||
            !AreValidThresholds(thresholds))
        {
            throw new ArgumentException("Duplicate aggregation requires two distinct graph nodes, one revision, valid JSON evidence, and one score per supported signal.", nameof(signals));
        }
    }

    private static bool AreValidThresholds(DuplicateSignalThresholds thresholds) => thresholds is not null && new[] { thresholds.Structural, thresholds.Signature, thresholds.Semantic }.All(static value => double.IsFinite(value) && value is >= 0d and <= 1d);
    private static (ArchitectureTarget Left, ArchitectureTarget Right) OrderTargets(ArchitectureTarget first, ArchitectureTarget second) => string.CompareOrdinal(first.StableId, second.StableId) <= 0 ? (first, second) : (second, first);

    private static string Rationale(IReadOnlyList<DuplicateSignalKind> qualified, IReadOnlyList<DuplicateSignalFact> signals, bool isLikelyDuplicate)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("algorithm", AggregationVersion);
            writer.WriteBoolean("isLikelyDuplicate", isLikelyDuplicate);
            writer.WriteStartArray("qualifiedSignals");
            foreach (var kind in qualified) writer.WriteStringValue(kind.ToString());
            writer.WriteEndArray();
            writer.WriteStartObject("scores");
            foreach (var signal in signals.OrderBy(static signal => signal.Kind)) writer.WriteNumber(signal.Kind.ToString(), signal.Score);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool IsJson(string value)
    {
        try { using var _ = JsonDocument.Parse(value); return true; }
        catch (JsonException) { return false; }
    }
}
