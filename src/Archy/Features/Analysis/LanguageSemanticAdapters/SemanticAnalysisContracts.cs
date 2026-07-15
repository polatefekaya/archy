namespace Archy.Features.Analysis.LanguageSemanticAdapters;

public sealed record SemanticAnalysisRequest(
    string SchemaVersion,
    string RepositoryRoot,
    string SnapshotId,
    IReadOnlyList<SemanticDocument> Documents);

public sealed record SemanticDocument(string RepositoryRelativePath, string ContentHash, string LanguageId);

public sealed record SemanticAnalysisResult(
    string SchemaVersion,
    string AdapterId,
    string Language,
    string SnapshotId,
    IReadOnlyList<SemanticCapability> Capabilities,
    IReadOnlyList<SemanticSymbol> Symbols,
    IReadOnlyList<SemanticDefinition> Definitions,
    IReadOnlyList<SemanticReference> References,
    IReadOnlyList<SemanticCall> Calls,
    IReadOnlyList<SemanticInheritance> Inheritance,
    IReadOnlyList<SemanticTypeFact> Types,
    IReadOnlyList<SemanticSyntaxSite> SyntaxSites,
    IReadOnlyList<SemanticDiagnostic> Diagnostics);

public sealed record SemanticSourceRange(
    string RepositoryRelativePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public sealed record SemanticSymbol(
    string CanonicalId,
    string DisplayName,
    SemanticSymbolKind Kind,
    string? ContainerCanonicalId,
    string Visibility,
    SemanticSourceRange Range,
    SemanticSourceRange? ScopeRange = null);

public sealed record SemanticDefinition(string SourceCanonicalId, string TargetCanonicalId, SemanticSourceRange Range);

public sealed record SemanticReference(string SourceCanonicalId, string TargetCanonicalId, SemanticSourceRange Range);

public sealed record SemanticCall(string CallerCanonicalId, string CalleeCanonicalId, SemanticSourceRange Range);

public sealed record SemanticInheritance(string DerivedCanonicalId, string BaseCanonicalId, SemanticSourceRange Range);

public sealed record SemanticTypeFact(string OwnerCanonicalId, string Role, string CanonicalTypeId, SemanticSourceRange Range);

public sealed record SemanticSyntaxSite(string Shape, IReadOnlyList<SemanticSyntaxCapture> Captures, SemanticSourceRange Range);

public sealed record SemanticSyntaxCapture(string Name, string RawValue, string Kind);

public sealed record SemanticDiagnostic(string Code, string Severity, string Message, SemanticSourceRange? Range);

public sealed record SemanticCapability(string Name, SemanticCapabilityState State, string? Detail);

public enum SemanticSymbolKind
{
    Namespace,
    Type,
    Method,
    Property,
    Field,
    Event,
    Parameter,
}

public enum SemanticCapabilityState
{
    Available,
    Unavailable,
    Degraded,
}
