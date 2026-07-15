using System.Text.Json;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

/// <summary>A live, initialized LSP session owned by one semantic-analysis attempt.</summary>
public interface ILanguageServerSession : ILanguageServerNotificationSink, IAsyncDisposable
{
    LanguageServerCapabilityProfile CapabilityProfile { get; }

    ValueTask<Result<JsonDocument>> SendRequestAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken);

    ValueTask<Problem?> ShutdownAsync(CancellationToken cancellationToken);

    LanguageServerStdioSessionDiagnostics GetDiagnostics();
}
