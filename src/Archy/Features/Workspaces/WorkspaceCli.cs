using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Mediator;

namespace Archy.Features.Workspaces;

public static class WorkspaceCli
{
    public static Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mediator);

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            WriteHelp();
            return Task.FromResult(0);
        }

        return args[0] switch
        {
            "locate" => LocateWorkspaceCli.RunAsync(args[1..], mediator, cancellationToken),
            "init" => InitializeWorkspaceCli.RunAsync(args[1..], mediator, cancellationToken),
            _ => UnknownWorkspaceCommandAsync(args[0]),
        };
    }

    private static Task<int> UnknownWorkspaceCommandAsync(string command)
    {
        Console.Error.WriteLine($"Unknown workspace command '{command}'. Run 'archy --help' for available commands.");
        return Task.FromResult(64);
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Usage: archy workspace <locate|init> [options]");
    }
}
