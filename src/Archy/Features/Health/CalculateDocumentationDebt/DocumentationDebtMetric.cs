namespace Archy.Features.Health.CalculateDocumentationDebt;
public sealed record DocumentationDebtMetric(long GraphRevision,int EligibleNodeCount,int StaleNodeCount,long OldestStaleAge,IReadOnlyList<string> StaleStableIds);
