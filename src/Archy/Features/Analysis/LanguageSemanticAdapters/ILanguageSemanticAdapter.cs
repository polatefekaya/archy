using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.LanguageSemanticAdapters;

public interface ILanguageSemanticAdapter
{
    string AdapterId { get; }

    string Language { get; }

    ValueTask<Result<SemanticAnalysisResult>> AnalyzeAsync(
        SemanticAnalysisRequest request,
        CancellationToken cancellationToken);
}
