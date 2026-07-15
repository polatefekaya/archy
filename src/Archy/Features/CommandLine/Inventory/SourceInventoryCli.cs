using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Analysis.InventorySources;
using Mediator;

namespace Archy.Features.CommandLine.Inventory;

public static partial class SourceInventoryCli
{
    public static async Task<int> RunAsync(string[] args, IMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mediator);
        var options = ParseOptions(args);
        if (options is null)
        {
            Console.Error.WriteLine("Usage: archy inventory [--path <path>] [--state-root <path>] [--config <path>] [--json]");
            return 64;
        }

        var result = await mediator.Send(
            new InventoryWorkspaceSourcesCommand(options.Path, options.ConfigurationPath, options.StateRoot),
            cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(options.Json, result.Problem!.Code, result.Problem.Message);
            return 2;
        }

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result.Value, SourceInventoryJsonContext.Default.SourceInventory));
        }
        else
        {
            Console.WriteLine($"Inventory complete: {result.Value.IsComplete}");
            Console.WriteLine($"Files: {result.Value.Files.Count}");
            Console.WriteLine($"Added: {result.Value.Changes.Count(static change => change.Kind == SourceFileChangeKind.Added)}");
            Console.WriteLine($"Changed: {result.Value.Changes.Count(static change => change.Kind == SourceFileChangeKind.Changed)}");
            Console.WriteLine($"Deleted: {result.Value.Changes.Count(static change => change.Kind == SourceFileChangeKind.Deleted)}");
            Console.WriteLine($"C# parse candidates: {result.Value.ParseCandidates.Count}");
            Console.WriteLine($"Excluded: {result.Value.Exclusions.Count}");
            Console.WriteLine($"Diagnostics: {result.Value.Diagnostics.Count}");
        }

        return result.Value.IsComplete ? 0 : 1;
    }

    private static SourceInventoryCliOptions? ParseOptions(string[] args)
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

        return new SourceInventoryCliOptions(path, stateRoot, configurationPath, json);
    }

    private static void WriteError(bool json, string code, string message)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new SourceInventoryError(code, message), SourceInventoryJsonContext.Default.SourceInventoryError));
            return;
        }

        Console.Error.WriteLine($"{code}: {message}");
    }

    private sealed record SourceInventoryCliOptions(string Path, string? StateRoot, string? ConfigurationPath, bool Json);

    private sealed record SourceInventoryError(string Code, string Message);

    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(SourceInventory))]
    [JsonSerializable(typeof(SourceInventoryError))]
    private sealed partial class SourceInventoryJsonContext : JsonSerializerContext;
}
