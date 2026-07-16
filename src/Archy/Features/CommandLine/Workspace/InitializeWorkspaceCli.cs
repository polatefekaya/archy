using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.CommandLine.TerminalPresentation;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Workspace;

public static partial class InitializeWorkspaceCli
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
            new InitializeWorkspaceCommand(
                options.Value.Path,
                options.Value.StateRoot,
                options.Value.ConfigurationPath),
            cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(options.Value.Json, result.Problem!);
            return 2;
        }

        WriteSuccess(options.Value.Json, result.Value);
        return 0;
    }

    private static Result<InitializeWorkspaceOptions> ParseOptions(string[] args)
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
                    return ResultFactory.Failure<InitializeWorkspaceOptions>(
                        Problem.Validation($"Unknown or incomplete workspace init option '{args[index]}'."));
            }
        }

        return ResultFactory.Success(new InitializeWorkspaceOptions(path, stateRoot, configurationPath, json));
    }

    private static void WriteSuccess(bool json, InitializedWorkspace workspace)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new InitializeWorkspaceResponse(
                    workspace.StateLocation.WorkspaceId,
                    workspace.StateLocation.StateDirectory,
                    workspace.StateLocation.ManifestPath,
                    workspace.WasCreated),
                InitializeWorkspaceJsonContext.Default.InitializeWorkspaceResponse));
            return;
        }

        TerminalCardWriter.WriteToStandardOutput(new TerminalCard(
            "ARCHY  ·  WORKSPACE READY",
            workspace.WasCreated ? "Local architectural memory has been initialized." : "Local architectural memory is already initialized.",
            [
                new TerminalDetail("Workspace ID", workspace.StateLocation.WorkspaceId),
                new TerminalDetail("State directory", workspace.StateLocation.StateDirectory),
                new TerminalDetail("Manifest", workspace.StateLocation.ManifestPath),
            ],
            "Next: archy analyze  ·  archy web serve  ·  archy mcp stdio"));
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new InitializeWorkspaceErrorResponse(problem.Code, problem.Message),
                InitializeWorkspaceJsonContext.Default.InitializeWorkspaceErrorResponse));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Usage: archy workspace init [--path <path>] [--state-root <path>] [--config <path>] [--json]");
    }

    private sealed record InitializeWorkspaceOptions(
        string Path,
        string? StateRoot,
        string? ConfigurationPath,
        bool Json);

    private sealed record InitializeWorkspaceResponse(
        string WorkspaceId,
        string StateDirectory,
        string ManifestPath,
        bool WasCreated);

    private sealed record InitializeWorkspaceErrorResponse(string Code, string Message);

    [JsonSerializable(typeof(InitializeWorkspaceResponse))]
    [JsonSerializable(typeof(InitializeWorkspaceErrorResponse))]
    private sealed partial class InitializeWorkspaceJsonContext : JsonSerializerContext;
}
