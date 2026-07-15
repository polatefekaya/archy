using Archy.Features.Decisions.ArchitectureDecisions;

namespace Archy.Features.Duplicates.ApplyDuplicateDecisionFeedback;

public interface IDuplicateDecisionFeedbackPolicy
{
    DuplicateDecisionFeedback Evaluate(string findingId, IReadOnlyList<ArchitectureDecision> decisions);
}
