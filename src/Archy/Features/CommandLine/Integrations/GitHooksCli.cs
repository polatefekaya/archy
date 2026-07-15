using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Integrations.GitHooks;
using Archy.Features.Integrations.GitHooks.Inspect;
using Archy.Features.Integrations.GitHooks.Install;
using Archy.Features.Integrations.GitHooks.Uninstall;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Integrations;

/// <summary>Owns the explicit install, status, and uninstall lifecycle for local Git enforcement hooks.</summary>
public static partial class GitHooksCli
{
    public static async Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mediator);
        if (args.Length == 0 || args[0] is not ("install" or "uninstall" or "status"))
        {
            return WriteUsage();
        }

        var options = ParseOptions(args[1..]);
        if (options is null)
        {
            return WriteUsage();
        }

        var result = args[0] switch
        {
            "install" => await mediator.Send(new InstallGitHooksCommand(options.Path), cancellationToken),
            "uninstall" => await mediator.Send(new UninstallGitHooksCommand(options.Path), cancellationToken),
            "status" => await mediator.Send(new InspectGitHooksCommand(options.Path), cancellationToken),
            _ => throw new InvalidOperationException("The validated Git hook operation was not dispatchable."),
        };
        if (!result.IsSuccess)
        {
            WriteError(options.Json, result.Problem!);
            return 2;
        }

        WriteInstallation(args[0], options.Json, result.Value);
        return 0;
    }

    private static GitHooksCliOptions? ParseOptions(string[] args)
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
                    return null;
            }
        }

        return new GitHooksCliOptions(path, json);
    }

    private static int WriteUsage()
    {
        Console.Error.WriteLine("Usage: archy hooks install|uninstall|status [--path <path>] [--json]");
        return 64;
    }

    private static void WriteInstallation(string operation, bool json, GitHookInstallation installation)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                installation,
                GitHooksCliJsonContext.Default.GitHookInstallation));
            return;
        }

        Console.WriteLine($"Archy Git hooks {operation} result for {installation.RepositoryRoot}.");
        Console.WriteLine($"Hook directory: {installation.HooksDirectory}{(installation.UsesCustomHooksPath ? " (core.hooksPath)" : string.Empty)}");
        foreach (var hook in installation.Hooks)
        {
            var state = hook.IsManagedByArchy ? "managed" : "not managed";
            var preservation = hook.HasPreservedHook ? "; preserved existing hook" : string.Empty;
            Console.WriteLine($"{hook.Kind}: {state}{preservation}");
        }
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new GitHooksCliError(problem.Code, problem.Message),
                GitHooksCliJsonContext.Default.GitHooksCliError));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private sealed record GitHooksCliOptions(string Path, bool Json);

    private sealed record GitHooksCliError(string Code, string Message);

    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(GitHookInstallation))]
    [JsonSerializable(typeof(GitHooksCliError))]
    private sealed partial class GitHooksCliJsonContext : JsonSerializerContext;
}
