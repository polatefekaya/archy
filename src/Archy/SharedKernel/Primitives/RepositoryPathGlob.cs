namespace Archy.SharedKernel.Primitives;

/// <summary>
/// Deterministic repository-relative glob matcher shared by source scope and architecture rules.
/// Supports <c>*</c>, <c>?</c>, and a whole-segment <c>**</c> recursive wildcard.
/// </summary>
public sealed class RepositoryPathGlob
{
    private readonly string[] segments;
    private readonly bool matchAnySegment;

    public RepositoryPathGlob(string pattern, bool matchAnySegment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var normalized = Normalize(pattern);
        segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("A repository glob must contain a relative path pattern.", nameof(pattern));
        }

        this.matchAnySegment = matchAnySegment;
    }

    public bool IsMatch(string repositoryRelativePath)
    {
        var pathSegments = Normalize(repositoryRelativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (matchAnySegment && segments.Length == 1)
        {
            return pathSegments.Any(segment => IsSegmentMatch(segments[0], segment));
        }

        return IsPathMatch(patternIndex: 0, pathIndex: 0, pathSegments);
    }

    public static string Normalize(string value) =>
        value.Replace('\\', '/').Trim('/');

    private bool IsPathMatch(int patternIndex, int pathIndex, string[] pathSegments)
    {
        if (patternIndex == segments.Length)
        {
            return pathIndex == pathSegments.Length;
        }

        var patternSegment = segments[patternIndex];
        if (patternSegment == "**")
        {
            if (patternIndex + 1 == segments.Length)
            {
                return true;
            }

            for (var candidatePathIndex = pathIndex; candidatePathIndex <= pathSegments.Length; candidatePathIndex++)
            {
                if (IsPathMatch(patternIndex + 1, candidatePathIndex, pathSegments))
                {
                    return true;
                }
            }

            return false;
        }

        return pathIndex < pathSegments.Length &&
               IsSegmentMatch(patternSegment, pathSegments[pathIndex]) &&
               IsPathMatch(patternIndex + 1, pathIndex + 1, pathSegments);
    }

    private static bool IsSegmentMatch(string pattern, string value)
    {
        var patternIndex = 0;
        var valueIndex = 0;
        var wildcardIndex = -1;
        var backtrackValueIndex = 0;
        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' || pattern[patternIndex] == value[valueIndex]))
            {
                patternIndex++;
                valueIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                wildcardIndex = patternIndex++;
                backtrackValueIndex = valueIndex;
                continue;
            }

            if (wildcardIndex >= 0)
            {
                patternIndex = wildcardIndex + 1;
                valueIndex = ++backtrackValueIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
