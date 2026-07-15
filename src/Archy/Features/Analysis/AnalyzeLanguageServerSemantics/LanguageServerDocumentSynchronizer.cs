using System.Text.Json;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

/// <summary>Synchronizes one immutable snapshot to the one LSP session that analyzes it.</summary>
internal static class LanguageServerDocumentSynchronizer
{
    public static async ValueTask<Problem?> OpenAsync(ILanguageServerNotificationSink sink, string repositoryRoot, string languageId, IReadOnlyList<LanguageServerDocumentBuffer> documents, CancellationToken cancellationToken)
    {
        foreach (var document in documents.OrderBy(static document => document.RepositoryRelativePath, StringComparer.Ordinal))
        {
            var parameters = JsonSerializer.SerializeToElement(
                new LspDidOpenTextDocumentParameters(new LspOpenTextDocumentItem(UriFor(repositoryRoot, document.RepositoryRelativePath), languageId, 1, document.Text)),
                LanguageServerDocumentSynchronizationJsonContext.Default.LspDidOpenTextDocumentParameters);
            var problem = await sink.SendNotificationAsync("textDocument/didOpen", parameters, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    public static async ValueTask<Problem?> CloseAsync(ILanguageServerNotificationSink sink, string repositoryRoot, IReadOnlyList<LanguageServerDocumentBuffer> documents, CancellationToken cancellationToken)
    {
        foreach (var document in documents.OrderBy(static document => document.RepositoryRelativePath, StringComparer.Ordinal))
        {
            var parameters = JsonSerializer.SerializeToElement(
                new LspDidCloseTextDocumentParameters(new LspTextDocumentIdentifier(UriFor(repositoryRoot, document.RepositoryRelativePath))),
                LanguageServerDocumentSynchronizationJsonContext.Default.LspDidCloseTextDocumentParameters);
            var problem = await sink.SendNotificationAsync("textDocument/didClose", parameters, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static string UriFor(string repositoryRoot, string relativePath) => new Uri(Path.Combine(Path.GetFullPath(repositoryRoot), relativePath)).AbsoluteUri;

    internal sealed record LspTextDocumentIdentifier(string Uri);

    internal sealed record LspOpenTextDocumentItem(string Uri, string LanguageId, int Version, string Text);

    internal sealed record LspDidOpenTextDocumentParameters(LspOpenTextDocumentItem TextDocument);

    internal sealed record LspDidCloseTextDocumentParameters(LspTextDocumentIdentifier TextDocument);
}
