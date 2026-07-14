using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Mediator;

namespace Archy.Features.Configuration;

public static class ConfigurationCli
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
            "show" => LoadEffectiveConfigurationCli.RunAsync(args[1..], mediator, cancellationToken),
            _ => UnknownConfigurationCommandAsync(args[0]),
        };
    }

    private static Task<int> UnknownConfigurationCommandAsync(string command)
    {
        Console.Error.WriteLine($"Unknown config command '{command}'. Run 'archy --help' for available commands.");
        return Task.FromResult(64);
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Usage: archy config show [--path <path>] [--config <path>] [--state-root <path>] [--json]");
    }
}
