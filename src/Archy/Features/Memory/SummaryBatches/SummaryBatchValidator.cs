using System.Text.Json;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Memory.SummaryBatches;

internal static class SummaryBatchValidator
{
    internal static Problem? Validate(SummaryBatchFact fact)
    {
        if (string.IsNullOrWhiteSpace(fact.SummaryBatchId) ||
            string.IsNullOrWhiteSpace(fact.SessionId) ||
            string.IsNullOrWhiteSpace(fact.SettleReason) ||
            fact.SourceGraphRevision < 1 ||
            !Enum.IsDefined(fact.RequestState) ||
            !IsJson(fact.ModelRequestMetadataJson) ||
            fact.Members is null ||
            fact.Members.Count == 0)
        {
            return Problem.Validation(
                "Summary batches require an ID, session, settle reason, request state, source graph revision, model metadata, and at least one member.");
        }

        var targetKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < fact.Members.Count; index++)
        {
            var member = fact.Members[index];
            if (string.IsNullOrWhiteSpace(member.TargetKind) ||
                string.IsNullOrWhiteSpace(member.TargetStableId) ||
                member.TouchOrdinal < 1 ||
                member.CoTouchedMemberOrdinals is null ||
                !targetKeys.Add($"{member.TargetKind}\u001f{member.TargetStableId}"))
            {
                return Problem.Validation("Summary batch members require unique targets, positive touch order, and an explicit co-touch list.");
            }

            var memberOrdinal = index + 1;
            var coTouched = member.CoTouchedMemberOrdinals;
            if (coTouched.Any(ordinal => ordinal < 1 || ordinal > fact.Members.Count || ordinal == memberOrdinal) ||
                coTouched.Distinct().Count() != coTouched.Count)
            {
                return Problem.Validation("Summary batch co-touch ordinals must reference distinct other members in the same batch.");
            }
        }

        for (var index = 0; index < fact.Members.Count; index++)
        {
            var memberOrdinal = index + 1;
            foreach (var coTouchedOrdinal in fact.Members[index].CoTouchedMemberOrdinals)
            {
                if (!fact.Members[coTouchedOrdinal - 1].CoTouchedMemberOrdinals.Contains(memberOrdinal))
                {
                    return Problem.Validation("Summary batch co-touch relationships must be symmetric.");
                }
            }
        }

        return null;
    }

    private static bool IsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
