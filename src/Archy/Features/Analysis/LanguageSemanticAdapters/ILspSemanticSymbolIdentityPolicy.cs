namespace Archy.Features.Analysis.LanguageSemanticAdapters;

public interface ILspSemanticSymbolIdentityPolicy
{
    string AdapterId { get; }

    SemanticSymbol? CreateSymbol(
        string repositoryRelativePath,
        string qualifiedName,
        string displayName,
        int lspSymbolKind,
        string? containerCanonicalId,
        SemanticSourceRange range);
}
