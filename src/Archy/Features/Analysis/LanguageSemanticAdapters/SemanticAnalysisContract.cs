using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.LanguageSemanticAdapters;

public static class SemanticAnalysisContract
{
    public const string CurrentSchemaVersion = "language-semantic/v2";

    public static Problem? ValidateRequest(SemanticAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.RepositoryRoot) ||
            string.IsNullOrWhiteSpace(request.SnapshotId) ||
            request.Documents is null ||
            request.Documents.Any(static document => document is null || string.IsNullOrWhiteSpace(document.ContentHash) || string.IsNullOrWhiteSpace(document.LanguageId) || !IsRelativePath(document.RepositoryRelativePath)))
        {
            return Problem.Validation("Semantic analysis requests require the current schema, snapshot, repository root, and hash-verified repository-relative documents.");
        }

        return null;
    }

    public static Problem? ValidateResult(SemanticAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(result.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(result.AdapterId) ||
            string.IsNullOrWhiteSpace(result.Language) ||
            string.IsNullOrWhiteSpace(result.SnapshotId) ||
            result.Capabilities is null ||
            result.Symbols is null ||
            result.Definitions is null || result.References is null || result.Calls is null || result.Inheritance is null || result.Types is null || result.SyntaxSites is null || result.Diagnostics is null ||
            result.Symbols.Any(static symbol => string.IsNullOrWhiteSpace(symbol.CanonicalId) || string.IsNullOrWhiteSpace(symbol.DisplayName) || string.IsNullOrWhiteSpace(symbol.Visibility) || !ValidRange(symbol.Range) || (symbol.ScopeRange is not null && (!ValidRange(symbol.ScopeRange) || !string.Equals(symbol.ScopeRange.RepositoryRelativePath, symbol.Range.RepositoryRelativePath, StringComparison.Ordinal)))) ||
            result.Definitions.Any(static definition => !ValidEdge(definition.SourceCanonicalId, definition.TargetCanonicalId, definition.Range)) ||
            result.References.Any(static reference => !ValidEdge(reference.SourceCanonicalId, reference.TargetCanonicalId, reference.Range)) ||
            result.Calls.Any(static call => !ValidEdge(call.CallerCanonicalId, call.CalleeCanonicalId, call.Range)) ||
            result.Inheritance.Any(static edge => !ValidEdge(edge.DerivedCanonicalId, edge.BaseCanonicalId, edge.Range)) ||
            result.Types.Any(static type => string.IsNullOrWhiteSpace(type.OwnerCanonicalId) || string.IsNullOrWhiteSpace(type.Role) || string.IsNullOrWhiteSpace(type.CanonicalTypeId) || !ValidRange(type.Range)) ||
            result.SyntaxSites.Any(static site => string.IsNullOrWhiteSpace(site.Shape) || site.Captures is null || !ValidRange(site.Range)) ||
            result.Diagnostics.Any(static diagnostic => string.IsNullOrWhiteSpace(diagnostic.Code) || string.IsNullOrWhiteSpace(diagnostic.Severity) || string.IsNullOrWhiteSpace(diagnostic.Message) || (diagnostic.Range is not null && !ValidRange(diagnostic.Range))) ||
            result.Capabilities.Any(static capability => string.IsNullOrWhiteSpace(capability.Name)))
        {
            return Problem.Validation("Semantic analysis results require canonical identities, valid source ranges, capabilities, and structured diagnostics.");
        }

        return null;
    }

    private static bool ValidEdge(string source, string target, SemanticSourceRange range) => !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target) && ValidRange(range);

    private static bool ValidRange(SemanticSourceRange range) => range is not null && IsRelativePath(range.RepositoryRelativePath) && range.StartLine > 0 && range.StartColumn > 0 && range.EndLine >= range.StartLine && range.EndColumn > 0 && (range.EndLine != range.StartLine || range.EndColumn >= range.StartColumn);

    private static bool IsRelativePath(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment == "..");
}
