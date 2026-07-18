using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Duplicates.IndexEmbeddings;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Embeddings;

public static partial class EmbeddingStatusCli
{
    public static async Task<int> RunAsync(string[] args, IMediator mediator, CancellationToken cancellationToken)
    {
        var options = Parse(args); if (options is null) return Usage();
        var initialized = await mediator.Send(new InitializeWorkspaceCommand(options.Path, options.StateRoot, options.ConfigurationPath), cancellationToken);
        if (!initialized.IsSuccess) return Error(options.Json, initialized.Problem!);
        var configuration = await mediator.Send(new LoadEffectiveConfigurationQuery(options.Path, options.ConfigurationPath, options.StateRoot), cancellationToken);
        if (!configuration.IsSuccess) return Error(options.Json, configuration.Problem!);
        var model = options.Model ?? configuration.Value.Configuration.Model.EmbeddingModel;
        if (string.IsNullOrWhiteSpace(model)) return Error(options.Json, Problem.Validation("An embedding model must be configured or supplied with --model."));
        var result = await mediator.Send(new EmbeddingIndexStatusQuery(initialized.Value.StateLocation, configuration.Value.Configuration, model), cancellationToken);
        if (!result.IsSuccess) return Error(options.Json, result.Problem!);
        if (options.Json) Console.WriteLine(JsonSerializer.Serialize(result.Value, EmbeddingStatusJsonContext.Default.EmbeddingIndexStatus));
        else Console.WriteLine($"Embedding cache: {result.Value.CachedVectorCount} vectors for {model}; active graph revision: {result.Value.GraphRevision?.ToString() ?? "none"}.");
        return 0;
    }
    private static Options? Parse(string[] args) { var path = Directory.GetCurrentDirectory(); string? stateRoot = null; string? config = null; string? model = null; var json = false; for (var i = 0; i < args.Length; i++) switch (args[i]) { case "--path" when i + 1 < args.Length: path = args[++i]; break; case "--state-root" when i + 1 < args.Length: stateRoot = args[++i]; break; case "--config" when i + 1 < args.Length: config = args[++i]; break; case "--model" when i + 1 < args.Length: model = args[++i]; break; case "--json": json = true; break; default: return null; } return new(path, stateRoot, config, model, json); }
    private static int Usage() { Console.Error.WriteLine("Usage: archy embeddings status [--path <path>] [--state-root <path>] [--config <path>] [--model <model-id>] [--json]"); return 64; }
    private static int Error(bool json, Problem problem) { if (json) Console.WriteLine(JsonSerializer.Serialize(new CliError(problem.Code, problem.Message), EmbeddingStatusJsonContext.Default.CliError)); else Console.Error.WriteLine($"{problem.Code}: {problem.Message}"); return 2; }
    private sealed record Options(string Path, string? StateRoot, string? ConfigurationPath, string? Model, bool Json); private sealed record CliError(string Code, string Message);
    [JsonSerializable(typeof(EmbeddingIndexStatus))] [JsonSerializable(typeof(CliError))] private sealed partial class EmbeddingStatusJsonContext : JsonSerializerContext;
}
