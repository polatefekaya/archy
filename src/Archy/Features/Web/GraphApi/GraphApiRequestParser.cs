using Microsoft.AspNetCore.Http;

namespace Archy.Features.Web.GraphApi;

/// <summary>Parses graph API query values before any state is opened.</summary>
public static class GraphApiRequestParser
{
    public const int DefaultPageLimit = 100;
    public const int MaximumPageLimit = 500;
    public const int MaximumOffset = 1_000_000;
    public const int DefaultTraversalDepth = 3;
    public const int MaximumTraversalDepth = 6;

    public static bool TryParsePage(HttpRequest request, out long? revision, out int offset, out int limit, out string? error)
    {
        revision = null;
        offset = 0;
        limit = DefaultPageLimit;
        error = null;
        if (!TryParseRevision(request, out revision, out error)) return false;
        if (!TryParseInt(request, "offset", 0, MaximumOffset, out offset, out error)) return false;
        return TryParseInt(request, "limit", 1, MaximumPageLimit, out limit, out error, DefaultPageLimit);
    }

    public static bool TryParseTraversal(HttpRequest request, out long? revision, out int depth, out string? error)
    {
        revision = null;
        depth = DefaultTraversalDepth;
        error = null;
        if (!TryParseRevision(request, out revision, out error)) return false;
        return TryParseInt(request, "depth", 1, MaximumTraversalDepth, out depth, out error, DefaultTraversalDepth);
    }

    private static bool TryParseRevision(HttpRequest request, out long? revision, out string? error)
    {
        revision = null;
        error = null;
        if (!request.Query.TryGetValue("revision", out var raw) || string.IsNullOrWhiteSpace(raw)) return true;
        if (raw.Count != 1 || !IsCanonicalUnsignedInteger(raw[0]) || !long.TryParse(raw[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
        {
            error = "Query parameter 'revision' must be one positive integer.";
            return false;
        }

        revision = parsed;
        return true;
    }

    private static bool TryParseInt(HttpRequest request, string name, int minimum, int maximum, out int value, out string? error, int? defaultValue = null)
    {
        value = defaultValue ?? minimum;
        error = null;
        if (!request.Query.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw)) return true;
        if (raw.Count != 1 || !IsCanonicalUnsignedInteger(raw[0]) || !int.TryParse(raw[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
        {
            error = $"Query parameter '{name}' must be one integer between {minimum} and {maximum}.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool IsCanonicalUnsignedInteger(string? value) =>
        !string.IsNullOrEmpty(value)
        && (value.Length == 1 || value[0] != '0')
        && value.All(static character => character is >= '0' and <= '9');
}
