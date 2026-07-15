using System.Xml.Linq;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.DiscoverCSharpProjects;

public sealed class CSharpProjectDiscovery : ICSharpProjectDiscovery
{
    public async ValueTask<Result<CSharpProjectMap>> DiscoverAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            return ResultFactory.Failure<CSharpProjectMap>(Problem.NotFound($"Repository root '{root}' does not exist."));
        }

        var solutions = new List<string>();
        var projectPaths = new List<string>();
        foreach (var path in EnumerateFiles(root, cancellationToken))
        {
            var relative = Relative(root, path);
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                solutions.Add(relative);
            }
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                projectPaths.Add(path);
            }
        }

        var projects = new List<CSharpProject>();
        var diagnostics = new List<CSharpProjectDiscoveryDiagnostic>();
        foreach (var projectPath in projectPaths.OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await ParseProjectAsync(root, projectPath, cancellationToken);
            if (!parsed.IsSuccess)
            {
                diagnostics.Add(new CSharpProjectDiscoveryDiagnostic(Relative(root, projectPath), parsed.Problem!.Code, parsed.Problem.Message));
                continue;
            }

            projects.Add(parsed.Value);
        }

        return ResultFactory.Success(new CSharpProjectMap(
            root,
            [.. solutions.OrderBy(static path => path, StringComparer.Ordinal)],
            projects,
            [.. diagnostics.OrderBy(static diagnostic => diagnostic.RepositoryRelativePath, StringComparer.Ordinal)]));
    }

    private static async Task<Result<CSharpProject>> ParseProjectAsync(string repositoryRoot, string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var targetFrameworks = Elements(document, "TargetFrameworks")
                .SelectMany(static value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Concat(Elements(document, "TargetFramework"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            var references = document.Descendants().Where(static element => element.Name.LocalName == "ProjectReference")
                .Select(static element => element.Attribute("Include")?.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Relative(repositoryRoot, Path.GetFullPath(Path.Combine(projectDirectory, value!))))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            var sourceRoots = document.Descendants().Where(static element => element.Name.LocalName == "Compile")
                .Select(static element => element.Attribute("Include")?.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(include => SourceRoot(repositoryRoot, projectDirectory, include!))
                .Append(Relative(repositoryRoot, projectDirectory))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
            var defaultCompileItems = OptionalBoolean(Elements(document, "EnableDefaultCompileItems").LastOrDefault(), defaultValue: true);
            var emitGenerated = OptionalBoolean(Elements(document, "EmitCompilerGeneratedFiles").LastOrDefault(), defaultValue: false);
            var generatedOutput = Elements(document, "CompilerGeneratedFilesOutputPath").LastOrDefault();
            return ResultFactory.Success(new CSharpProject(
                Relative(repositoryRoot, projectPath),
                targetFrameworks,
                references,
                sourceRoots,
                new CSharpGeneratedCodeSettings(defaultCompileItems, emitGenerated, generatedOutput)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return ResultFactory.Failure<CSharpProject>(Problem.Validation($"Archy could not parse C# project metadata: {exception.Message}"));
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(static path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(entry))
                {
                    var name = Path.GetFileName(entry);
                    if (name is ".git" or "bin" or "obj")
                    {
                        continue;
                    }

                    directories.Push(entry);
                    continue;
                }

                yield return entry;
            }
        }
    }

    private static IEnumerable<string> Elements(XDocument document, string name) => document.Descendants().Where(element => element.Name.LocalName == name).Select(static element => element.Value.Trim()).Where(static value => value.Length > 0);

    private static bool OptionalBoolean(string? value, bool defaultValue) => bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static string SourceRoot(string repositoryRoot, string projectDirectory, string include)
    {
        var prefix = include.Split(['*', '?'], 2)[0].TrimEnd('/', '\\');
        return Relative(repositoryRoot, prefix.Length == 0 ? projectDirectory : Path.GetFullPath(Path.Combine(projectDirectory, prefix)));
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
