using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;

public sealed class LanguageServerStdioSessionFactory : ILanguageServerSessionFactory
{
    public async ValueTask<Result<ILanguageServerSession>> StartAsync(
        LanguageServerLaunchSpecification specification,
        int? clientProcessId,
        CancellationToken cancellationToken)
    {
        var started = await LanguageServerStdioSession.StartAsync(specification, clientProcessId, cancellationToken);
        return started.IsSuccess
            ? ResultFactory.Success<ILanguageServerSession>(started.Value)
            : ResultFactory.Failure<ILanguageServerSession>(started.Problem!);
    }
}
