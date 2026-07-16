using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.Features.CommandLine.TerminalPresentation;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Analysis;

public static partial class AnalyzeWorkspaceCli
{
    public static async Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mediator);
        var options = ParseOptions(args);
        if (options is null)
        {
            Console.Error.WriteLine("Usage: archy analyze [--path <path>] [--state-root <path>] [--config <path>] [--json]");
            return 64;
        }

        if (!options.Json)
        {
            AnalysisTerminalScreen.WriteStarting(options.Path);
        }

        var result = await mediator.Send(
            new AnalyzeWorkspaceCommand(options.Path, options.ConfigurationPath, options.StateRoot),
            cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(options.Json, result.Problem!);
            return 2;
        }

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result.Value, AnalyzeWorkspaceJsonContext.Default.WorkspaceAnalysis));
        }
        else
        {
            AnalysisTerminalScreen.WriteCompleted(result.Value);
        }

        return result.Value.IsComplete ? 0 : 1;
    }

    private static AnalyzeWorkspaceCliOptions? ParseOptions(string[] args)
    {
        var path = Directory.GetCurrentDirectory();
        string? stateRoot = null;
        string? configurationPath = null;
        var json = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--path" when index + 1 < args.Length:
                    path = args[++index];
                    break;
                case "--state-root" when index + 1 < args.Length:
                    stateRoot = args[++index];
                    break;
                case "--config" when index + 1 < args.Length:
                    configurationPath = args[++index];
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return null;
            }
        }

        return new AnalyzeWorkspaceCliOptions(path, stateRoot, configurationPath, json);
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new AnalyzeWorkspaceError(problem.Code, problem.Message),
                AnalyzeWorkspaceJsonContext.Default.AnalyzeWorkspaceError));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private sealed record AnalyzeWorkspaceCliOptions(
        string Path,
        string? StateRoot,
        string? ConfigurationPath,
        bool Json);

    private sealed record AnalyzeWorkspaceError(string Code, string Message);

    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(WorkspaceAnalysis))]
    [JsonSerializable(typeof(AnalyzeWorkspaceError))]
    private sealed partial class AnalyzeWorkspaceJsonContext : JsonSerializerContext;
}
