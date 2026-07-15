namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

public sealed record LanguageServerDocumentBuffer(
    string RepositoryRelativePath,
    string ContentHash,
    string Text);
