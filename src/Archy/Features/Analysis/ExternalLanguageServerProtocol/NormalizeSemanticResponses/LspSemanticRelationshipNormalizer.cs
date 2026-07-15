using System.Text.Json;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.NormalizeSemanticResponses;

/// <summary>
/// Normalizes standard Language Server Protocol definition and reference responses into
/// language-neutral semantic relationships. The selected language adapter owns symbol identity.
/// </summary>
public static class LspSemanticRelationshipNormalizer
{
    public static Result<IReadOnlyList<SemanticCall>> ExtractCalls(string callerCanonicalId, string repositoryRoot, JsonElement definitionResponse, IReadOnlyList<SemanticSymbol> symbols) =>
        MapDefinitionTargets(callerCanonicalId, repositoryRoot, definitionResponse, symbols, static definition => new SemanticCall(definition.SourceCanonicalId, definition.TargetCanonicalId, definition.Range));

    public static Result<IReadOnlyList<SemanticInheritance>> ExtractInheritance(string derivedCanonicalId, string repositoryRoot, JsonElement definitionResponse, IReadOnlyList<SemanticSymbol> symbols) =>
        MapDefinitionTargets(derivedCanonicalId, repositoryRoot, definitionResponse, symbols, static definition => new SemanticInheritance(definition.SourceCanonicalId, definition.TargetCanonicalId, definition.Range));

    public static Result<IReadOnlyList<SemanticReference>> ExtractReferences(string targetCanonicalId, string repositoryRoot, JsonElement referencesResponse, IReadOnlyList<SemanticSymbol> symbols)
    {
        if (string.IsNullOrWhiteSpace(targetCanonicalId) || !Path.IsPathFullyQualified(repositoryRoot) || symbols is null || referencesResponse.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
        {
            return ResultFactory.Failure<IReadOnlyList<SemanticReference>>(Problem.Validation("LSP reference extraction requires a target identity, repository root, location array, and known semantic symbols."));
        }

        if (referencesResponse.ValueKind == JsonValueKind.Null)
        {
            return ResultFactory.Success<IReadOnlyList<SemanticReference>>([]);
        }

        var references = new List<SemanticReference>();
        foreach (var location in referencesResponse.EnumerateArray())
        {
            var range = LspSemanticResponseNormalizer.ResolveLocation(repositoryRoot, location);
            if (!range.IsSuccess)
            {
                return ResultFactory.Failure<IReadOnlyList<SemanticReference>>(range.Problem!);
            }

            var source = FindSmallestContainingScope(symbols, range.Value);
            if (source is not null && !string.Equals(source.CanonicalId, targetCanonicalId, StringComparison.Ordinal))
            {
                references.Add(new SemanticReference(source.CanonicalId, targetCanonicalId, range.Value));
            }
        }

        return ResultFactory.Success<IReadOnlyList<SemanticReference>>([.. references.Distinct().OrderBy(static reference => reference.SourceCanonicalId, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Normalizes <c>callHierarchy/outgoingCalls</c> results. Call-hierarchy items identify
    /// their target with a Location-like shape; each <c>fromRanges</c> value is relative to
    /// the caller's document.
    /// </summary>
    public static Result<IReadOnlyList<SemanticCall>> ExtractOutgoingCalls(
        string callerCanonicalId,
        string repositoryRoot,
        JsonElement outgoingCallsResponse,
        IReadOnlyList<SemanticSymbol> symbols)
    {
        if (string.IsNullOrWhiteSpace(callerCanonicalId) || !Path.IsPathFullyQualified(repositoryRoot) || symbols is null || outgoingCallsResponse.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
        {
            return ResultFactory.Failure<IReadOnlyList<SemanticCall>>(Problem.Validation("LSP outgoing-call extraction requires a caller identity, repository root, array result, and known semantic symbols."));
        }

        var caller = symbols.SingleOrDefault(symbol => string.Equals(symbol.CanonicalId, callerCanonicalId, StringComparison.Ordinal));
        if (caller is null)
        {
            return ResultFactory.Failure<IReadOnlyList<SemanticCall>>(Problem.Validation("LSP outgoing-call extraction requires the caller to be part of the semantic symbol batch."));
        }

        if (outgoingCallsResponse.ValueKind == JsonValueKind.Null)
        {
            return ResultFactory.Success<IReadOnlyList<SemanticCall>>([]);
        }

        var calls = new List<SemanticCall>();
        foreach (var outgoingCall in outgoingCallsResponse.EnumerateArray())
        {
            if (outgoingCall.ValueKind != JsonValueKind.Object ||
                !outgoingCall.TryGetProperty("to", out var targetItem) ||
                !outgoingCall.TryGetProperty("fromRanges", out var fromRanges) ||
                fromRanges.ValueKind != JsonValueKind.Array)
            {
                return ResultFactory.Failure<IReadOnlyList<SemanticCall>>(Problem.Validation("LSP outgoing-call entries must contain a target item and an array of caller ranges."));
            }

            var targetLocation = LspSemanticResponseNormalizer.ResolveLocation(repositoryRoot, targetItem);
            if (!targetLocation.IsSuccess)
            {
                return ResultFactory.Failure<IReadOnlyList<SemanticCall>>(targetLocation.Problem!);
            }

            var target = FindSmallestContainingSymbol(symbols, targetLocation.Value);
            foreach (var fromRange in fromRanges.EnumerateArray())
            {
                var evidence = LspSemanticResponseNormalizer.ResolveRange(caller.Range.RepositoryRelativePath, fromRange);
                if (!evidence.IsSuccess)
                {
                    return ResultFactory.Failure<IReadOnlyList<SemanticCall>>(evidence.Problem!);
                }

                if (target is not null)
                {
                    calls.Add(new SemanticCall(callerCanonicalId, target.CanonicalId, evidence.Value));
                }
            }
        }

        return ResultFactory.Success<IReadOnlyList<SemanticCall>>(
            [.. calls
                .Distinct()
                .OrderBy(static call => call.CallerCanonicalId, StringComparer.Ordinal)
                .ThenBy(static call => call.CalleeCanonicalId, StringComparer.Ordinal)
                .ThenBy(static call => call.Range.RepositoryRelativePath, StringComparer.Ordinal)
                .ThenBy(static call => call.Range.StartLine)
                .ThenBy(static call => call.Range.StartColumn)]);
    }

    private static Result<IReadOnlyList<T>> MapDefinitionTargets<T>(string sourceCanonicalId, string repositoryRoot, JsonElement response, IReadOnlyList<SemanticSymbol> symbols, Func<SemanticDefinition, T> map)
    {
        var definitions = LspSemanticResponseNormalizer.ExtractDefinitions(sourceCanonicalId, repositoryRoot, response, symbols);
        return definitions.IsSuccess
            ? ResultFactory.Success<IReadOnlyList<T>>([.. definitions.Value.Select(map)])
            : ResultFactory.Failure<IReadOnlyList<T>>(definitions.Problem!);
    }

    private static bool Contains(SemanticSourceRange range, int line, int column) => (line > range.StartLine || line == range.StartLine && column >= range.StartColumn) && (line < range.EndLine || line == range.EndLine && column <= range.EndColumn);

    private static SemanticSymbol? FindSmallestContainingSymbol(IReadOnlyList<SemanticSymbol> symbols, SemanticSourceRange range) =>
        symbols
            .Where(symbol => string.Equals(symbol.Range.RepositoryRelativePath, range.RepositoryRelativePath, StringComparison.Ordinal) && Contains(symbol.Range, range.StartLine, range.StartColumn))
            .OrderBy(symbol => Span(symbol.Range))
            .ThenBy(static symbol => symbol.CanonicalId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static SemanticSymbol? FindSmallestContainingScope(IReadOnlyList<SemanticSymbol> symbols, SemanticSourceRange range) =>
        symbols
            .Where(symbol =>
            {
                var scope = symbol.ScopeRange ?? symbol.Range;
                return string.Equals(scope.RepositoryRelativePath, range.RepositoryRelativePath, StringComparison.Ordinal) && Contains(scope, range.StartLine, range.StartColumn);
            })
            .OrderBy(symbol => Span(symbol.ScopeRange ?? symbol.Range))
            .ThenBy(static symbol => symbol.CanonicalId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static long Span(SemanticSourceRange range) => ((long)range.EndLine - range.StartLine) * 1_000_000L + range.EndColumn - range.StartColumn;
}
