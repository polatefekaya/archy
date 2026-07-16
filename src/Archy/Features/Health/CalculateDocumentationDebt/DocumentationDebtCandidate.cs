namespace Archy.Features.Health.CalculateDocumentationDebt;
public sealed record DocumentationDebtCandidate(string StableId,long LastSummaryRevision,bool IsEligible,bool AiEnabled);
