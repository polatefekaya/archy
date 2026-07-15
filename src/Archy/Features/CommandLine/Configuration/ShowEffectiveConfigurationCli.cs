using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Configuration;

public static partial class ShowEffectiveConfigurationCli
{
    public static async Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mediator);

        if (args.Length == 1 && args[0] is "--help" or "-h" or "help")
        {
            WriteHelp();
            return 0;
        }

        var options = ParseOptions(args);
        if (!options.IsSuccess)
        {
            Console.Error.WriteLine(options.Problem!.Message);
            return 64;
        }

        var result = await mediator.Send(
            new LoadEffectiveConfigurationQuery(
                options.Value.Path,
                options.Value.ConfigurationPath,
                options.Value.StateRoot),
            cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(options.Value.Json, result.Problem!);
            return 2;
        }

        WriteSuccess(options.Value.Json, result.Value);
        return 0;
    }

    private static Result<LoadEffectiveConfigurationOptions> ParseOptions(string[] args)
    {
        var path = Directory.GetCurrentDirectory();
        string? configurationPath = null;
        string? stateRoot = null;
        var json = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--path" when index + 1 < args.Length:
                    path = args[++index];
                    break;
                case "--config" when index + 1 < args.Length:
                    configurationPath = args[++index];
                    break;
                case "--state-root" when index + 1 < args.Length:
                    stateRoot = args[++index];
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ResultFactory.Failure<LoadEffectiveConfigurationOptions>(
                        Problem.Validation($"Unknown or incomplete config show option '{args[index]}'."));
            }
        }

        return ResultFactory.Success(
            new LoadEffectiveConfigurationOptions(path, configurationPath, stateRoot, json));
    }

    private static void WriteSuccess(bool json, EffectiveConfiguration configuration)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                configuration,
                ShowEffectiveConfigurationJsonContext.Default.EffectiveConfiguration));
            return;
        }

        Console.WriteLine($"Schema version: {configuration.Configuration.SchemaVersion}");
        Console.WriteLine($"Model provider: {configuration.Configuration.Model.Provider}");
        Console.WriteLine($"State root: {configuration.StateRoot ?? "default (~/.archy/workspaces)"}");
        Console.WriteLine("Applied configuration sources:");
        foreach (var source in configuration.Sources)
        {
            var path = source.Path is null ? "built in" : source.Path;
            Console.WriteLine($"  {source.Kind}: {path}");
        }
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new ShowEffectiveConfigurationErrorResponse(problem.Code, problem.Message),
                ShowEffectiveConfigurationJsonContext.Default.ShowEffectiveConfigurationErrorResponse));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Usage: archy config show [--path <path>] [--config <path>] [--state-root <path>] [--json]");
    }

    private sealed record LoadEffectiveConfigurationOptions(
        string Path,
        string? ConfigurationPath,
        string? StateRoot,
        bool Json);

    private sealed record ShowEffectiveConfigurationErrorResponse(string Code, string Message);

    [JsonSerializable(typeof(EffectiveConfiguration))]
    [JsonSerializable(typeof(ShowEffectiveConfigurationErrorResponse))]
    private sealed partial class ShowEffectiveConfigurationJsonContext : JsonSerializerContext;
}
