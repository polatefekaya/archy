namespace Archy.Features.Health.CalculateDocumentationDebt;
public interface IDocumentationDebtCalculator { DocumentationDebtMetric Calculate(long graphRevision,IReadOnlyList<DocumentationDebtCandidate> candidates); }
