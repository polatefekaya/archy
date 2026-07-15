namespace Archy.Features.Analysis.ExtractCSharpSyntaxFacts;

public sealed record CSharpSyntaxDiagnostic(
    string RepositoryRelativePath,
    string Code,
    string Severity,
    int Line,
    int Column,
    string Message);
