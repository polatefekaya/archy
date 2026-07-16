using Archy.Features.CommandLine.Analysis;
using Archy.Features.CommandLine.Architecture;
using Archy.Features.CommandLine.Configuration;
using Archy.Features.CommandLine.Inventory;
using Archy.Features.CommandLine.Integrations;
using Archy.Features.CommandLine.Integrations.CodexHooks;
using Archy.Features.CommandLine.Workspace;
using Archy.Features.CommandLine.WorkspaceDatabase;
using Archy.Features.CommandLine.Web;
using Mediator;

namespace Archy.Features.CommandLine;

public static class ArchyCli
{
    public static Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
        => RunAsync(args, mediator, serviceProvider: null, cancellationToken);

    public static Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        IServiceProvider? serviceProvider,
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
            "db" => WorkspaceDatabaseCli.RunAsync(args[1..], mediator, cancellationToken),
            "inventory" => SourceInventoryCli.RunAsync(args[1..], mediator, cancellationToken),
            "analyze" => AnalyzeWorkspaceCli.RunAsync(args[1..], mediator, cancellationToken),
            "verify" => VerifyArchitectureCli.RunAsync(args[1..], mediator, cancellationToken),
            "baseline" => ArchitectureBaselineCli.RunAsync(args[1..], mediator, cancellationToken),
            "exception" => ArchitectureExceptionCli.RunAsync(args[1..], mediator, cancellationToken),
            "hooks" => GitHooksCli.RunAsync(args[1..], mediator, cancellationToken),
            "mcp" => McpCli.RunAsync(args[1..], mediator, serviceProvider, cancellationToken),
            "web" => WebCli.RunAsync(args[1..], mediator, serviceProvider, cancellationToken),
            "codex-hook" => CodexHookCli.RunAsync(args[1..], mediator, serviceProvider, cancellationToken),
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
        Console.WriteLine("  archy inventory [--path <path>] [--state-root <path>] [--config <path>] [--json]");
        Console.WriteLine("  archy analyze [--path <path>] [--state-root <path>] [--config <path>] [--json]");
        Console.WriteLine("  archy verify [--path <path>] [--state-root <path>] [--config <path>] [--json | --sarif [--output <path>]]");
        Console.WriteLine("  archy baseline accept [--path <path>] [--state-root <path>] [--config <path>] [--json]");
        Console.WriteLine("  archy exception accept --finding <key> --author <author> --reason <reason> --review-at <ISO-8601> --expires-at <ISO-8601> [--path <path>] [--state-root <path>] [--config <path>] [--json]");
        Console.WriteLine("  archy hooks install|uninstall|status [--path <path>] [--json]");
        Console.WriteLine("  archy mcp stdio [<workspace-path>]");
        Console.WriteLine("  archy web serve [--port <1-65535>] [--path <path>]");
        Console.WriteLine("  archy db check|backup|restore|vacuum|diagnostics [--path <path>] [--state-root <path>] [--config <path>] [--json]");
    }
}
