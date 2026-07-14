using Archy.Features.Configuration;
using Archy.Features.Workspaces;
using Mediator;

namespace Archy.Features.CommandLine;

public static class ArchyCli
{
    public static Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args is ["--version"] or ["-V"])
        {
            Console.WriteLine($"Archy {ArchyProductMetadata.Version}");
            return Task.FromResult(0);
        }

        ArgumentNullException.ThrowIfNull(mediator);

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            WriteHelp();
            return Task.FromResult(0);
        }

        return args[0] switch
        {
            "workspace" => WorkspaceCli.RunAsync(args[1..], mediator, cancellationToken),
            "config" => ConfigurationCli.RunAsync(args[1..], mediator, cancellationToken),
            _ => UnknownCommandAsync(),
        };
    }

    private static Task<int> UnknownCommandAsync()
    {
        Console.Error.WriteLine("Unknown command. Run 'archy --help' for available commands.");
        return Task.FromResult(64);
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Archy — architectural memory for codebases");
        Console.WriteLine();
        Console.WriteLine("  archy --version");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  archy workspace locate [--path <path>] [--json]");
        Console.WriteLine("  archy workspace init [--path <path>] [--state-root <path>] [--config <path>] [--json]");
        Console.WriteLine("  archy config show [--path <path>] [--config <path>] [--state-root <path>] [--json]");
    }
}
