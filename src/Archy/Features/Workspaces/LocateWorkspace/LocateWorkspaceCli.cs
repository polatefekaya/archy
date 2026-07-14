using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Workspaces.LocateWorkspace;

public static partial class LocateWorkspaceCli
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
            new LocateWorkspaceCommand(options.Value.Path),
            cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(options.Value.Json, result.Problem!);
            return 2;
        }

        WriteSuccess(options.Value.Json, result.Value);
        return 0;
    }

    private static Result<LocateWorkspaceOptions> ParseOptions(string[] args)
    {
        var path = Directory.GetCurrentDirectory();
        var json = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--path" when index + 1 < args.Length:
                    path = args[++index];
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ResultFactory.Failure<LocateWorkspaceOptions>(
                        Problem.Validation($"Unknown or incomplete workspace locate option '{args[index]}'."));
            }
        }

        return ResultFactory.Success(new LocateWorkspaceOptions(path, json));
    }

    private static void WriteSuccess(bool json, LocatedWorkspace workspace)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new LocateWorkspaceResponse(
                    workspace.RepositoryRoot,
                    workspace.GitMetadataPath,
                    workspace.IsLinkedWorktree),
                LocateWorkspaceJsonContext.Default.LocateWorkspaceResponse));
            return;
        }

        Console.WriteLine($"Repository root: {workspace.RepositoryRoot}");
        Console.WriteLine($"Git metadata: {workspace.GitMetadataPath}");
        Console.WriteLine($"Linked worktree: {workspace.IsLinkedWorktree}");
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new LocateWorkspaceErrorResponse(problem.Code, problem.Message),
                LocateWorkspaceJsonContext.Default.LocateWorkspaceErrorResponse));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Archy — architectural memory for codebases");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  archy workspace locate [--path <path>] [--json]");
    }

    private sealed record LocateWorkspaceOptions(string Path, bool Json);

    private sealed record LocateWorkspaceResponse(
        string RepositoryRoot,
        string GitMetadataPath,
        bool IsLinkedWorktree);

    private sealed record LocateWorkspaceErrorResponse(string Code, string Message);

    [JsonSerializable(typeof(LocateWorkspaceResponse))]
    [JsonSerializable(typeof(LocateWorkspaceErrorResponse))]
    private sealed partial class LocateWorkspaceJsonContext : JsonSerializerContext;
}
