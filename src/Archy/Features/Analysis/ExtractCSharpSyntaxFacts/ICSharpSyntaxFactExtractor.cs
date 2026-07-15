using Archy.Features.Analysis.InventorySources;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExtractCSharpSyntaxFacts;

public interface ICSharpSyntaxFactExtractor
{
    ValueTask<Result<CSharpSyntaxFactBatch>> ExtractAsync(
        string repositoryRoot,
        IReadOnlyList<SourceFile> files,
        CancellationToken cancellationToken);
}
