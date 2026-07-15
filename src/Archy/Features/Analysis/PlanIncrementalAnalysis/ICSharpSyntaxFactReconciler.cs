using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.PlanIncrementalAnalysis;

public interface ICSharpSyntaxFactReconciler
{
    Result<CSharpSyntaxFactBatch> Reconcile(
        GraphRevisionSnapshot activeRevision,
        IncrementalAnalysisPlan plan,
        CSharpSyntaxFactBatch changedFacts);
}
