using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Architecture;

/// <summary>Owns explicit, repository-changing baseline acceptance commands.</summary>
public static partial class ArchitectureBaselineCli
{
    public static async Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mediator);
        if (args.Length == 0 || !string.Equals(args[0], "accept", StringComparison.Ordinal))
        {
            return WriteUsage();
        }

        var options = ParseOptions(args[1..]);
        if (options is null)
        {
            return WriteUsage();
        }

        var result = await mediator.Send(
            new AcceptArchitectureBaselineCommand(options.Path, options.ConfigurationPath, options.StateRoot),
            cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(options.Json, result.Problem!);
            return 2;
        }

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                result.Value,
                ArchitectureBaselineCliJsonContext.Default.AcceptedArchitectureBaseline));
        }
        else
        {
            Console.WriteLine($"Accepted {result.Value.FindingCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} architecture findings at graph revision {result.Value.GraphRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            Console.WriteLine($"Commit {result.Value.Path} so local verification and CI share this baseline.");
        }

        return 0;
    }

    private static ArchitectureBaselineCliOptions? ParseOptions(string[] args)
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

        return new ArchitectureBaselineCliOptions(path, stateRoot, configurationPath, json);
    }

    private static int WriteUsage()
    {
        Console.Error.WriteLine("Usage: archy baseline accept [--path <path>] [--state-root <path>] [--config <path>] [--json]");
        return 64;
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new ArchitectureBaselineError(problem.Code, problem.Message),
                ArchitectureBaselineCliJsonContext.Default.ArchitectureBaselineError));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private sealed record ArchitectureBaselineCliOptions(
        string Path,
        string? StateRoot,
        string? ConfigurationPath,
        bool Json);

    private sealed record ArchitectureBaselineError(string Code, string Message);

    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(AcceptedArchitectureBaseline))]
    [JsonSerializable(typeof(ArchitectureBaselineError))]
    private sealed partial class ArchitectureBaselineCliJsonContext : JsonSerializerContext;
}
