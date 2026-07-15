using System.Text.Json;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

public interface ILanguageServerNotificationSink
{
    ValueTask<Problem?> SendNotificationAsync(string method, JsonElement? parameters, CancellationToken cancellationToken);
}
