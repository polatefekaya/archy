using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpWorkspaceContextFactory(IMediator mediator)
{
    public async ValueTask<Result<McpWorkspaceContext>> CreateResultAsync(string startPath, CancellationToken cancellationToken)
    {
        var located = await mediator.Send(new LocateWorkspaceCommand(startPath), cancellationToken);
        if (!located.IsSuccess) return ResultFactory.Failure<McpWorkspaceContext>(located.Problem!);
        var initialized = await mediator.Send(new InitializeWorkspaceCommand(located.Value!.RepositoryRoot, null, null), cancellationToken);
        return initialized.IsSuccess
            ? ResultFactory.Success(new McpWorkspaceContext(located.Value.RepositoryRoot, initialized.Value!.StateLocation))
            : ResultFactory.Failure<McpWorkspaceContext>(initialized.Problem!);
    }

    public async ValueTask<McpWorkspaceContext?> CreateAsync(string startPath, CancellationToken cancellationToken)
    {
        var result = await CreateResultAsync(startPath, cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
