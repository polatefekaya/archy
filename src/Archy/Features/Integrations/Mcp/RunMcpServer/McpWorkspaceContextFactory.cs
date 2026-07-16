using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Mediator;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpWorkspaceContextFactory(IMediator mediator)
{
    public async ValueTask<McpWorkspaceContext?> CreateAsync(string startPath, CancellationToken cancellationToken)
    {
        var located = await mediator.Send(new LocateWorkspaceCommand(startPath), cancellationToken);
        if (!located.IsSuccess) return null;
        var initialized = await mediator.Send(new InitializeWorkspaceCommand(located.Value!.RepositoryRoot, null, null), cancellationToken);
        return initialized.IsSuccess ? new McpWorkspaceContext(located.Value.RepositoryRoot, initialized.Value!.StateLocation) : null;
    }
}
