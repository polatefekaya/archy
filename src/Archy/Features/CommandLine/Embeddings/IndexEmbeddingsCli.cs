using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Duplicates.IndexEmbeddings;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Embeddings;

public static partial class IndexEmbeddingsCli
{
    public static async Task<int> RunAsync(string[] args, IMediator mediator, CancellationToken cancellationToken)
    {
        var options = Parse(args);
        if (options is null) return Usage();
        var initialized = await mediator.Send(new InitializeWorkspaceCommand(options.Path, options.StateRoot, options.ConfigurationPath), cancellationToken);
        if (!initialized.IsSuccess) return Error(options.Json, initialized.Problem!);
        var workspace = await mediator.Send(new LocateWorkspaceCommand(options.Path), cancellationToken);
        if (!workspace.IsSuccess) return Error(options.Json, workspace.Problem!);
        var configuration = await mediator.Send(new LoadEffectiveConfigurationQuery(options.Path, options.ConfigurationPath, options.StateRoot), cancellationToken);
        if (!configuration.IsSuccess) return Error(options.Json, configuration.Problem!);
        var model = options.Model ?? configuration.Value.Configuration.Model.EmbeddingModel;
        if (string.IsNullOrWhiteSpace(model)) return Error(options.Json, Problem.Validation("An embedding model must be configured or supplied with --model."));
        var maxChunks = options.MaxChunks ?? Math.Clamp(configuration.Value.Configuration.Model.MaxRequestsPerRun, 1, 2048);
        var effective = configuration.Value.Configuration with { Model = configuration.Value.Configuration.Model with { EmbeddingModel = model } };
        var result = await mediator.Send(new IndexEmbeddingsCommand(initialized.Value.StateLocation, workspace.Value!.RepositoryRoot, effective, model, maxChunks, options.DryRun), cancellationToken);
        if (!result.IsSuccess) return Error(options.Json, result.Problem!);
        if (options.Json) Console.WriteLine(JsonSerializer.Serialize(result.Value, IndexEmbeddingsJsonContext.Default.EmbeddingIndexResult));
        else Console.WriteLine($"Embedding index {(options.DryRun ? "plan" : "complete")}: {result.Value.RequestedChunks} eligible, {result.Value.CacheHits} cached, {result.Value.RequestedChunks - result.Value.CacheHits} would send, {result.Value.Generated} generated.");
        return 0;
    }

    private static Options? Parse(string[] args)
    {
        var path = Directory.GetCurrentDirectory(); string? model = null; string? stateRoot = null; string? configurationPath = null; int? maxChunks = null; var dryRun = false; var json = false;
        for (var index = 0; index < args.Length; index++) switch (args[index])
        {
            case "--path" when index + 1 < args.Length: path = args[++index]; break;
            case "--state-root" when index + 1 < args.Length: stateRoot = args[++index]; break;
            case "--config" when index + 1 < args.Length: configurationPath = args[++index]; break;
            case "--model" when index + 1 < args.Length: model = args[++index]; break;
            case "--max-chunks" when index + 1 < args.Length && int.TryParse(args[++index], out var value) && value is >= 1 and <= 2048: maxChunks = value; break;
            case "--dry-run": dryRun = true; break;
            case "--json": json = true; break;
            default: return null;
        }
        return new(path, stateRoot, configurationPath, model, maxChunks, dryRun, json);
    }

    private static int Usage() { Console.Error.WriteLine("Usage: archy embeddings index [--path <path>] [--state-root <path>] [--config <path>] [--model <model-id>] [--max-chunks <1-2048>] [--dry-run] [--json]"); return 64; }
    private static int Error(bool json, Problem problem) { if (json) Console.WriteLine(JsonSerializer.Serialize(new CliError(problem.Code, problem.Message), IndexEmbeddingsJsonContext.Default.CliError)); else Console.Error.WriteLine($"{problem.Code}: {problem.Message}"); return 2; }
    private sealed record Options(string Path, string? StateRoot, string? ConfigurationPath, string? Model, int? MaxChunks, bool DryRun, bool Json);
    private sealed record CliError(string Code, string Message);
    [JsonSerializable(typeof(EmbeddingIndexResult))] [JsonSerializable(typeof(CliError))] private sealed partial class IndexEmbeddingsJsonContext : JsonSerializerContext;
}
