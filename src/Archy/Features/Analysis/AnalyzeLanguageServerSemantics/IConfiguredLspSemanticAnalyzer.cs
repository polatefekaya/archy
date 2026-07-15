using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

public interface IConfiguredLspSemanticAnalyzer
{
    ValueTask<Result<SemanticAnalysisResult>> AnalyzeAsync(ConfiguredLspSemanticAnalysisRequest request, CancellationToken cancellationToken);
}
