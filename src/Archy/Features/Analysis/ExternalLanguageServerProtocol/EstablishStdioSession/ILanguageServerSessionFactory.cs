using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

public interface ILanguageServerSessionFactory
{
    ValueTask<Result<ILanguageServerSession>> StartAsync(
        LanguageServerLaunchSpecification specification,
        int? clientProcessId,
        CancellationToken cancellationToken);
}
