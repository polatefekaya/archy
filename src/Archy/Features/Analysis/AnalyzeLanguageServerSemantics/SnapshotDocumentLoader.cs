using System.Security.Cryptography;
using System.Text;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

internal static class SnapshotDocumentLoader
{
    public static async ValueTask<Result<IReadOnlyList<LanguageServerDocumentBuffer>>> LoadAsync(SemanticAnalysisRequest request, CancellationToken cancellationToken)
    {
        var buffers = new List<LanguageServerDocumentBuffer>(request.Documents.Count);
        foreach (var document in request.Documents.OrderBy(static document => document.RepositoryRelativePath, StringComparer.Ordinal))
        {
            var path = ResolvePath(request.RepositoryRoot, document.RepositoryRelativePath);
            if (!path.IsSuccess)
            {
                return ResultFactory.Failure<IReadOnlyList<LanguageServerDocumentBuffer>>(path.Problem!);
            }

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ResultFactory.Failure<IReadOnlyList<LanguageServerDocumentBuffer>>(Problem.Storage($"Archy could not read source '{document.RepositoryRelativePath}': {exception.Message}"));
            }

            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), document.ContentHash, StringComparison.Ordinal))
            {
                return ResultFactory.Failure<IReadOnlyList<LanguageServerDocumentBuffer>>(Problem.Conflict($"Source '{document.RepositoryRelativePath}' changed after source inventory. Run analysis again."));
            }

            string text;
            try
            {
                await using var stream = new MemoryStream(bytes, writable: false);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                text = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (DecoderFallbackException exception)
            {
                return ResultFactory.Failure<IReadOnlyList<LanguageServerDocumentBuffer>>(Problem.Validation($"Source '{document.RepositoryRelativePath}' has an unsupported text encoding: {exception.Message}"));
            }

            buffers.Add(new LanguageServerDocumentBuffer(document.RepositoryRelativePath, document.ContentHash, text));
        }

        return ResultFactory.Success<IReadOnlyList<LanguageServerDocumentBuffer>>([.. buffers]);
    }

    private static Result<string> ResolvePath(string repositoryRoot, string repositoryRelativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, repositoryRelativePath));
        var prefix = string.Concat(root, Path.DirectorySeparatorChar);
        return Path.IsPathRooted(repositoryRelativePath) || !fullPath.StartsWith(prefix, StringComparison.Ordinal)
            ? ResultFactory.Failure<string>(Problem.Validation($"Source path '{repositoryRelativePath}' escapes the repository root."))
            : ResultFactory.Success(fullPath);
    }
}
