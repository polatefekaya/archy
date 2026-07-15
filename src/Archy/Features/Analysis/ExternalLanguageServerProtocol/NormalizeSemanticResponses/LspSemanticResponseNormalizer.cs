using System.Text.Json;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.NormalizeSemanticResponses;

/// <summary>
/// Normalizes standard Language Server Protocol document-symbol and definition responses.
/// Language-specific symbol identity and kind decisions are delegated to the selected adapter.
/// </summary>
public static class LspSemanticResponseNormalizer
{
    public static Result<IReadOnlyList<SemanticSymbol>> ExtractDocumentSymbols(
        string repositoryRelativePath,
        JsonElement response,
        ILspSemanticSymbolIdentityPolicy identityPolicy)
    {
        ArgumentNullException.ThrowIfNull(identityPolicy);
        if (!IsRelative(repositoryRelativePath) || response.ValueKind != JsonValueKind.Array)
        {
            return ResultFactory.Failure<IReadOnlyList<SemanticSymbol>>(Problem.Validation("LSP document-symbol responses require a repository-relative document and an array result."));
        }

        var symbols = new List<SemanticSymbol>();
        foreach (var value in response.EnumerateArray())
        {
            Extract(value, repositoryRelativePath, null, null, symbols, identityPolicy);
        }

        if (symbols.Select(static symbol => symbol.CanonicalId).Distinct(StringComparer.Ordinal).Count() != symbols.Count)
        {
            return ResultFactory.Failure<IReadOnlyList<SemanticSymbol>>(Problem.Conflict($"LSP adapter '{identityPolicy.AdapterId}' produced duplicate semantic symbol identities."));
        }

        return ResultFactory.Success<IReadOnlyList<SemanticSymbol>>([.. symbols.OrderBy(static symbol => symbol.CanonicalId, StringComparer.Ordinal)]);
    }

    public static Result<IReadOnlyList<SemanticDefinition>> ExtractDefinitions(
        string sourceCanonicalId,
        string repositoryRoot,
        JsonElement response,
        IReadOnlyList<SemanticSymbol> knownSymbols)
    {
        if (string.IsNullOrWhiteSpace(sourceCanonicalId) || !Path.IsPathFullyQualified(repositoryRoot) || knownSymbols is null || response.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Null))
        {
            return ResultFactory.Failure<IReadOnlyList<SemanticDefinition>>(Problem.Validation("LSP definition extraction requires a source identity, valid location result, and known semantic symbols."));
        }

        if (response.ValueKind == JsonValueKind.Null)
        {
            return ResultFactory.Success<IReadOnlyList<SemanticDefinition>>([]);
        }

        var locations = response.ValueKind == JsonValueKind.Array ? response.EnumerateArray() : new[] { response }.AsEnumerable();
        var definitions = new List<SemanticDefinition>();
        foreach (var location in locations)
        {
            var resolvedLocation = ResolveLocation(repositoryRoot, location);
            if (!resolvedLocation.IsSuccess)
            {
                return ResultFactory.Failure<IReadOnlyList<SemanticDefinition>>(Problem.Validation("LSP definition response contained an invalid Location or LocationLink."));
            }

            var range = resolvedLocation.Value;
            var target = knownSymbols
                .Where(symbol => string.Equals(symbol.Range.RepositoryRelativePath, range.RepositoryRelativePath, StringComparison.Ordinal) && Contains(symbol.Range, range.StartLine, range.StartColumn))
                .OrderBy(symbol => Span(symbol.Range))
                .ThenBy(static symbol => symbol.CanonicalId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (target is not null)
            {
                definitions.Add(new SemanticDefinition(sourceCanonicalId, target.CanonicalId, range));
            }
        }

        return ResultFactory.Success<IReadOnlyList<SemanticDefinition>>([.. definitions.Distinct().OrderBy(static definition => definition.TargetCanonicalId, StringComparer.Ordinal)]);
    }

    public static Result<SemanticSourceRange> ResolveLocation(string repositoryRoot, JsonElement location)
    {
        if (!Path.IsPathFullyQualified(repositoryRoot) || !TryLocation(location, repositoryRoot, out _, out var range))
        {
            return ResultFactory.Failure<SemanticSourceRange>(Problem.Validation("LSP Location or LocationLink must target a repository-relative file range."));
        }

        return ResultFactory.Success(range);
    }

    /// <summary>Converts one standard-LSP range into Archy's one-based source range.</summary>
    public static Result<SemanticSourceRange> ResolveRange(string repositoryRelativePath, JsonElement range)
    {
        if (!IsRelative(repositoryRelativePath) || !TryRangeValue(range, repositoryRelativePath, out var resolvedRange))
        {
            return ResultFactory.Failure<SemanticSourceRange>(Problem.Validation("LSP ranges must describe a repository-relative source span."));
        }

        return ResultFactory.Success(resolvedRange);
    }

    private static void Extract(JsonElement value, string path, string? containerId, string? containerName, List<SemanticSymbol> symbols, ILspSemanticSymbolIdentityPolicy identityPolicy)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("name", out var nameValue) || string.IsNullOrWhiteSpace(nameValue.GetString()) || !value.TryGetProperty("kind", out var kindValue) || !kindValue.TryGetInt32(out var lspKind) || !TryRange(value, "selectionRange", path, out var range))
        {
            return;
        }

        var name = nameValue.GetString()!;
        var qualifiedName = containerName is null ? name : string.Concat(containerName, ".", name);
        var symbol = identityPolicy.CreateSymbol(path, qualifiedName, name, lspKind, containerId, range);
        if (symbol is not null)
        {
            var scopeRange = TryRange(value, "range", path, out var parsedScopeRange) && parsedScopeRange != range
                ? parsedScopeRange
                : null;
            symbols.Add(symbol with { ScopeRange = scopeRange });
        }

        if (value.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Extract(child, path, symbol?.CanonicalId ?? containerId, qualifiedName, symbols, identityPolicy);
            }
        }
    }

    private static bool TryLocation(JsonElement location, string repositoryRoot, out string path, out SemanticSourceRange range)
    {
        path = string.Empty;
        range = null!;
        if (location.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var uriProperty = location.TryGetProperty("targetUri", out var targetUri) ? targetUri : location.TryGetProperty("uri", out var uri) ? uri : default;
        var rangeName = location.TryGetProperty("targetSelectionRange", out _)
            ? "targetSelectionRange"
            : location.TryGetProperty("selectionRange", out _)
                ? "selectionRange"
                : "range";
        if (uriProperty.ValueKind != JsonValueKind.String || !Uri.TryCreate(uriProperty.GetString(), UriKind.Absolute, out var parsed) || !parsed.IsFile || !TryRange(location, rangeName, string.Empty, out range))
        {
            return false;
        }

        path = Path.GetRelativePath(repositoryRoot, parsed.LocalPath).Replace(Path.DirectorySeparatorChar, '/');
        if (!IsRelative(path))
        {
            return false;
        }

        range = range with { RepositoryRelativePath = path };
        return true;
    }

    private static bool TryRange(JsonElement value, string propertyName, string path, out SemanticSourceRange range)
    {
        range = null!;
        return value.TryGetProperty(propertyName, out var raw) && TryRangeValue(raw, path, out range);
    }

    private static bool TryRangeValue(JsonElement raw, string path, out SemanticSourceRange range)
    {
        range = null!;
        if (raw.ValueKind != JsonValueKind.Object ||
            !TryPosition(raw, "start", out var startLine, out var startColumn) || !TryPosition(raw, "end", out var endLine, out var endColumn))
        {
            return false;
        }

        range = new SemanticSourceRange(path, startLine + 1, startColumn + 1, endLine + 1, endColumn + 1);
        return true;
    }

    private static bool TryPosition(JsonElement range, string name, out int line, out int character)
    {
        line = 0;
        character = 0;
        return range.TryGetProperty(name, out var position) && position.ValueKind == JsonValueKind.Object && position.TryGetProperty("line", out var lineValue) && lineValue.TryGetInt32(out line) && line >= 0 && position.TryGetProperty("character", out var characterValue) && characterValue.TryGetInt32(out character) && character >= 0;
    }

    private static bool Contains(SemanticSourceRange range, int line, int column) => (line > range.StartLine || line == range.StartLine && column >= range.StartColumn) && (line < range.EndLine || line == range.EndLine && column <= range.EndColumn);

    private static long Span(SemanticSourceRange range) => ((long)range.EndLine - range.StartLine) * 1_000_000L + range.EndColumn - range.StartColumn;

    private static bool IsRelative(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static part => part == "..");
}
