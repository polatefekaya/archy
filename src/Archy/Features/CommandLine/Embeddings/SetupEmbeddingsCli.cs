using Archy.Features.Workspaces.LocateWorkspace;
using Mediator;

namespace Archy.Features.CommandLine.Embeddings;

/// <summary>Explicitly records repository consent before source-derived content can be sent to an embedding provider.</summary>
public static class SetupEmbeddingsCli
{
    public static async Task<int> RunAsync(string[] args, IMediator mediator, CancellationToken cancellationToken)
    {
        var options = Parse(args);
        if (options is null) return Usage();
        if (!options.AllowSourceSharing) return Error("Refusing to enable embeddings without --allow-source-sharing. This explicitly permits source-derived embedding requests to the configured provider.");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))) return Error("OPENAI_API_KEY is required before enabling the OpenAI embedding provider.");
        var workspace = await mediator.Send(new LocateWorkspaceCommand(options.Path), cancellationToken);
        if (!workspace.IsSuccess) return Error(workspace.Problem!.Message);
        var configPath = options.ConfigurationPath is null
            ? Path.Combine(workspace.Value!.RepositoryRoot, "archy.toml")
            : Path.GetFullPath(options.ConfigurationPath);
        try
        {
            var existing = File.Exists(configPath) ? await File.ReadAllTextAsync(configPath, cancellationToken) : string.Empty;
            var updated = ConfigureEmbeddingSetup.Apply(existing, options.Provider, options.Model);
            var directory = Path.GetDirectoryName(configPath);
            if (string.IsNullOrWhiteSpace(directory)) return Error("The configuration path must have a parent directory.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(configPath)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporary, updated, cancellationToken);
            File.Move(temporary, configPath, overwrite: true);
            Console.WriteLine($"Embeddings enabled for OpenAI model '{options.Model}'. Configuration: {configPath}");
            Console.WriteLine("Next: archy analyze --path . && archy embeddings index --path .");
            return 0;
        }
        catch (ArgumentException exception) { return Error(exception.Message); }
        catch (IOException exception) { return Error($"Could not update embedding configuration: {exception.Message}"); }
    }

    private static Options? Parse(string[] args)
    {
        var path = Directory.GetCurrentDirectory(); var model = "text-embedding-3-large"; var provider = "openai"; string? configurationPath = null; var allowSourceSharing = false;
        for (var index = 0; index < args.Length; index++) switch (args[index])
        {
            case "--path" when index + 1 < args.Length: path = args[++index]; break;
            case "--model" when index + 1 < args.Length: model = args[++index]; break;
            case "--provider" when index + 1 < args.Length: provider = args[++index]; break;
            case "--config" when index + 1 < args.Length: configurationPath = args[++index]; break;
            case "--allow-source-sharing": allowSourceSharing = true; break;
            default: return null;
        }
        return new Options(path, model, provider, configurationPath, allowSourceSharing);
    }

    private static int Usage() { Console.Error.WriteLine("Usage: archy embeddings setup --allow-source-sharing [--path <path>] [--config <path>] [--provider openai] [--model text-embedding-3-large]"); return 64; }
    private static int Error(string message) { Console.Error.WriteLine($"validation: {message}"); return 2; }
    private sealed record Options(string Path, string Model, string Provider, string? ConfigurationPath, bool AllowSourceSharing);
}
