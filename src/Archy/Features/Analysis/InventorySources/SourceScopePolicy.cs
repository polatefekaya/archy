using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.InventorySources;

public sealed class SourceScopePolicy
{
    private static readonly string[] GeneratedDirectoryNames = ["bin", "obj"];

    private readonly IReadOnlyList<RepositoryPathGlob> includes;
    private readonly IReadOnlyList<RepositoryPathGlob> excludes;
    private readonly IReadOnlyList<GitIgnoreRule> gitIgnoreRules;
    private readonly string? stateDirectoryRelativePath;

    private SourceScopePolicy(
        IReadOnlyList<RepositoryPathGlob> includes,
        IReadOnlyList<RepositoryPathGlob> excludes,
        IReadOnlyList<GitIgnoreRule> gitIgnoreRules,
        string? stateDirectoryRelativePath)
    {
        this.includes = includes;
        this.excludes = excludes;
        this.gitIgnoreRules = gitIgnoreRules;
        this.stateDirectoryRelativePath = stateDirectoryRelativePath;
    }

    public static async ValueTask<Result<SourceScopePolicy>> CreateAsync(
        string repositoryRoot,
        string stateDirectory,
        ScopeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            var root = Path.GetFullPath(repositoryRoot);
            var includes = configuration.Include
                .Select(static pattern => new RepositoryPathGlob(pattern, HasNoDirectorySeparator(pattern)))
                .ToArray();
            var excludes = configuration.Exclude
                .Select(static pattern => new RepositoryPathGlob(pattern, HasNoDirectorySeparator(pattern)))
                .ToArray();
            var stateDirectoryRelativePath = TryGetRepositoryRelativePath(root, stateDirectory);
            var rules = await LoadGitIgnoreRulesAsync(root, stateDirectoryRelativePath, cancellationToken);
            return ResultFactory.Success(new SourceScopePolicy(
                includes,
                excludes,
                rules,
                stateDirectoryRelativePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ResultFactory.Failure<SourceScopePolicy>(
                Problem.Storage($"Archy could not load repository scope rules: {exception.Message}"));
        }
    }

    public SourcePathDecision Explain(string repositoryRelativePath, bool isDirectory)
    {
        var path = NormalizeRepositoryRelativePath(repositoryRelativePath);
        if (HasPathSegment(path, ".git"))
        {
            return SourcePathDecision.Exclude(SourcePathExclusionReason.GitMetadata, ".git");
        }

        if (stateDirectoryRelativePath is not null && IsSameOrDescendant(path, stateDirectoryRelativePath))
        {
            return SourcePathDecision.Exclude(SourcePathExclusionReason.ArchyState, stateDirectoryRelativePath);
        }

        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(GeneratedDirectoryNames.Contains))
        {
            return SourcePathDecision.Exclude(SourcePathExclusionReason.GeneratedDirectory, "built-in:bin,obj");
        }

        var ignored = ResolveGitIgnore(path, isDirectory);
        if (ignored is not null)
        {
            return SourcePathDecision.Exclude(SourcePathExclusionReason.GitIgnore, ignored.DisplayRule);
        }

        if (includes.Count > 0 && !includes.Any(glob => glob.IsMatch(path)))
        {
            return SourcePathDecision.Exclude(SourcePathExclusionReason.ScopeInclude, "scope.include");
        }

        var exclude = excludes.FirstOrDefault(glob => glob.IsMatch(path));
        return exclude is null
            ? SourcePathDecision.Include()
            : SourcePathDecision.Exclude(SourcePathExclusionReason.ScopeExclude, "scope.exclude");
    }

    internal static string NormalizeRepositoryRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = RepositoryPathGlob.Normalize(path);
        if (normalized.Length == 0 ||
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("The path must be a repository-relative path.", nameof(path));
        }

        return normalized;
    }

    private GitIgnoreRule? ResolveGitIgnore(string path, bool isDirectory)
    {
        GitIgnoreRule? match = null;
        foreach (var rule in gitIgnoreRules)
        {
            if (rule.Matches(path, isDirectory))
            {
                match = rule;
            }
        }

        return match is { IsNegated: false } ? match : null;
    }

    private static async Task<IReadOnlyList<GitIgnoreRule>> LoadGitIgnoreRulesAsync(
        string repositoryRoot,
        string? stateDirectoryRelativePath,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        var directories = new Stack<string>();
        directories.Push(repositoryRoot);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relativePath = NormalizeRepositoryRelativePath(Path.GetRelativePath(repositoryRoot, entry));
                if (IsBuiltInExcluded(relativePath, stateDirectoryRelativePath))
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(entry);
                }
                else if (string.Equals(Path.GetFileName(entry), ".gitignore", StringComparison.Ordinal))
                {
                    candidates.Add(entry);
                }
            }
        }

        var rules = new List<GitIgnoreRule>();
        foreach (var candidate in candidates.OrderBy(
                     path => Path.GetRelativePath(repositoryRoot, Path.GetDirectoryName(path)!),
                     StringComparer.Ordinal))
        {
            var baseDirectory = Path.GetRelativePath(repositoryRoot, Path.GetDirectoryName(candidate)!);
            var normalizedBaseDirectory = baseDirectory == "."
                ? string.Empty
                : NormalizeRepositoryRelativePath(baseDirectory);
            var lines = await File.ReadAllLinesAsync(candidate, cancellationToken);
            for (var index = 0; index < lines.Length; index++)
            {
                var parsed = GitIgnoreRule.TryParse(normalizedBaseDirectory, candidate, index + 1, lines[index]);
                if (parsed is not null)
                {
                    rules.Add(parsed);
                }
            }
        }

        return rules
            .OrderBy(static rule => rule.BaseDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).Length)
            .ThenBy(static rule => rule.BaseDirectory, StringComparer.Ordinal)
            .ThenBy(static rule => rule.LineNumber)
            .ToArray();
    }

    private static bool HasNoDirectorySeparator(string pattern) =>
        !pattern.TrimStart('/').Contains('/', StringComparison.Ordinal);

    private static string? TryGetRepositoryRelativePath(string repositoryRoot, string candidatePath)
    {
        var relative = Path.GetRelativePath(repositoryRoot, Path.GetFullPath(candidatePath));
        if (relative == "." ||
            (!relative.Equals("..", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            return relative == "." ? string.Empty : NormalizeRepositoryRelativePath(relative);
        }

        return null;
    }

    private static bool HasPathSegment(string path, string expectedSegment) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains(expectedSegment, StringComparer.Ordinal);

    private static bool IsBuiltInExcluded(string repositoryRelativePath, string? stateDirectoryRelativePath) =>
        HasPathSegment(repositoryRelativePath, ".git") ||
        repositoryRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(GeneratedDirectoryNames.Contains) ||
        (stateDirectoryRelativePath is not null &&
         IsSameOrDescendant(repositoryRelativePath, stateDirectoryRelativePath));

    private static bool IsSameOrDescendant(string path, string ancestor) =>
        ancestor.Length == 0 ||
        string.Equals(path, ancestor, StringComparison.Ordinal) ||
        path.StartsWith($"{ancestor}/", StringComparison.Ordinal);

    private sealed class GitIgnoreRule(
        string baseDirectory,
        int lineNumber,
        string displayRule,
        bool isNegated,
        bool directoryOnly,
        RepositoryPathGlob glob)
    {
        internal string BaseDirectory { get; } = baseDirectory;

        internal int LineNumber { get; } = lineNumber;

        internal string DisplayRule { get; } = displayRule;

        internal bool IsNegated { get; } = isNegated;

        internal bool Matches(string repositoryRelativePath, bool isDirectory)
        {
            if (!IsSameOrDescendant(repositoryRelativePath, BaseDirectory))
            {
                return false;
            }

            var localPath = BaseDirectory.Length == 0
                ? repositoryRelativePath
                : repositoryRelativePath[(BaseDirectory.Length + 1)..];
            if (!directoryOnly)
            {
                return glob.IsMatch(localPath);
            }

            var segments = localPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var finalSegmentCount = isDirectory ? segments.Length : segments.Length - 1;
            for (var count = 1; count <= finalSegmentCount; count++)
            {
                if (glob.IsMatch(string.Join('/', segments[..count])))
                {
                    return true;
                }
            }

            return false;
        }

        internal static GitIgnoreRule? TryParse(string baseDirectory, string sourcePath, int lineNumber, string line)
        {
            if (string.IsNullOrEmpty(line) || (line[0] == '#' && !line.StartsWith("\\#", StringComparison.Ordinal)))
            {
                return null;
            }

            var value = line;
            var isNegated = value[0] == '!' && !value.StartsWith("\\!", StringComparison.Ordinal);
            if (isNegated)
            {
                value = value[1..];
            }
            else if (value.StartsWith("\\#", StringComparison.Ordinal) || value.StartsWith("\\!", StringComparison.Ordinal))
            {
                value = value[1..];
            }

            var directoryOnly = value.EndsWith('/');
            value = value.TrimEnd('/');
            var anchored = value.StartsWith('/');
            value = value.TrimStart('/');
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return new GitIgnoreRule(
                baseDirectory,
                lineNumber,
                $"{sourcePath}:{lineNumber}:{line}",
                isNegated,
                directoryOnly,
                new RepositoryPathGlob(value, matchAnySegment: !anchored && HasNoDirectorySeparator(value)));
        }
    }
}
