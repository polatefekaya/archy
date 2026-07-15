using System.Text.Json;
using Archy.Features.Decisions.ArchitectureDecisions;
using Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ComposeDuplicateInspection;

/// <summary>Composes side-by-side evidence for human review and strips model vectors from every signal payload.</summary>
public sealed class DuplicateInspectionComposer(IDuplicateDecisionFeedbackPolicy feedbackPolicy) : IDuplicateInspectionComposer
{
    public DuplicateInspection Compose(DuplicateInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Observation);
        ArgumentNullException.ThrowIfNull(request.Graph);
        ArgumentNullException.ThrowIfNull(request.Decisions);
        if (request.Observation.GraphRevision != request.Graph.Revision || request.Decisions.Any(static decision => decision is null))
        {
            throw new ArgumentException("Duplicate inspection requires one matching graph revision and complete decision history.", nameof(request));
        }

        var left = Node(request.Graph.Nodes, request.Observation.LeftTarget.StableId);
        var right = Node(request.Graph.Nodes, request.Observation.RightTarget.StableId);
        if (left is null || right is null) throw new ArgumentException("The selected graph revision no longer contains both duplicate targets.", nameof(request));
        var decisions = request.Decisions
            .Where(decision => decision.Targets.Any(target => target.Kind == ArchitectureTargetKind.DuplicateFinding && string.Equals(target.StableId, request.Observation.FindingId, StringComparison.Ordinal)))
            .OrderByDescending(static decision => decision.OccurredAtUtc)
            .ThenByDescending(static decision => decision.DecisionId, StringComparer.Ordinal)
            .ToArray();
        var feedback = feedbackPolicy.Evaluate(request.Observation.FindingId, decisions);
        return new DuplicateInspection(
            request.Observation.FindingId,
            request.Observation.GraphRevision,
            request.Observation.Confidence,
            EvidenceSanitizer.Sanitize(request.Observation.RationaleJson),
            Side(left),
            Side(right),
            [.. request.Observation.Signals.OrderBy(static signal => signal.Kind).Select(static signal => new DuplicateInspectionSignal(signal.Kind, signal.Score, EvidenceSanitizer.Sanitize(signal.EvidenceJson)))],
            decisions,
            feedback.SuppressRepeatedFinding ? "Review the latest exact-pair ignore decision before reopening this finding." : "Review the side-by-side evidence, then record an accepted, ignored, or modified duplicate decision.");
    }

    private static GraphNodeFact? Node(IReadOnlyList<GraphNodeFact> nodes, string stableId) => nodes.SingleOrDefault(node => string.Equals(node.StableId, stableId, StringComparison.Ordinal));
    private static DuplicateInspectionSide Side(GraphNodeFact node) => new(node.StableId, node.DisplayName, node.FilePath, node.StartLine, node.EndLine);

    private static class EvidenceSanitizer
    {
        private static readonly HashSet<string> BlockedNames = new(StringComparer.OrdinalIgnoreCase) { "vector", "vectors", "embedding", "embeddings" };

        public static string Sanitize(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream)) Write(writer, document.RootElement);
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (JsonException)
            {
                return "{\"evidenceUnavailable\":true}";
            }
        }

        private static void Write(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        if (BlockedNames.Contains(property.Name)) continue;
                        writer.WritePropertyName(property.Name);
                        Write(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray()) Write(writer, item);
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }
    }
}
