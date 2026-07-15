using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Analysis.AnalyzeWorkspace;
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
        else if (result.Value.WasNoOp)
        {
            Console.WriteLine("Analysis is current; no graph revision was needed.");
        }
        else
        {
            Console.WriteLine($"Analysis complete: {result.Value.IsComplete}");
            Console.WriteLine($"C# syntax diagnostics: {result.Value.CSharpSyntaxFacts?.Diagnostics.Count ?? 0}");
            Console.WriteLine($"Resolved DI registrations: {result.Value.DependencyRegistrationFacts?.Edges.Count ?? 0}");
            WriteDiagnostics("DI registration diagnostics", result.Value.DependencyRegistrationFacts?.Diagnostics);
            Console.WriteLine($"Resolved DI consumptions: {result.Value.DependencyConsumptionFacts?.Edges.Count ?? 0}");
            WriteDiagnostics("DI consumption diagnostics", result.Value.DependencyConsumptionFacts?.Diagnostics);
            Console.WriteLine($"Resolved configuration reads: {result.Value.ConfigurationReadFacts?.Edges.Count ?? 0}");
            WriteDiagnostics("Configuration diagnostics", result.Value.ConfigurationReadFacts?.Diagnostics);
            Console.WriteLine($"Resolved configuration definitions: {result.Value.ConfigurationDefinitionFacts?.Edges.Count ?? 0}");
            WriteDiagnostics("Configuration definition diagnostics", result.Value.ConfigurationDefinitionFacts?.Diagnostics);
            Console.WriteLine($"Resolved RabbitMQ topology edges: {result.Value.RabbitMqTopologyFacts?.Edges.Count ?? 0}");
            WriteDiagnostics("RabbitMQ topology diagnostics", result.Value.RabbitMqTopologyFacts?.Diagnostics);
            Console.WriteLine($"Resolved message contracts: {result.Value.MessageContractFacts?.Edges.Count ?? 0}");
            WriteDiagnostics("Message contract diagnostics", result.Value.MessageContractFacts?.Diagnostics);
            Console.WriteLine($"Graph revision: {result.Value.GraphRevision?.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}");
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

    private static void WriteDiagnostics<TDiagnostic>(
        string label,
        IReadOnlyList<TDiagnostic>? diagnostics)
        where TDiagnostic : class
    {
        Console.WriteLine($"{label}: {diagnostics?.Count ?? 0}");
        if (diagnostics is null)
        {
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            Console.WriteLine(diagnostic);
        }
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
