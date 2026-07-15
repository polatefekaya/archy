using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Architecture.ExportSarif;
using Archy.Features.Architecture.VerifyArchitecture;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Architecture;

public static partial class VerifyArchitectureCli
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
            Console.Error.WriteLine("Usage: archy verify [--path <path>] [--state-root <path>] [--config <path>] [--json | --sarif [--output <path>]]");
            return 64;
        }

        var result = await mediator.Send(
            new VerifyArchitectureCommand(options.Path, options.ConfigurationPath, options.StateRoot),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return WriteError(options, result.Problem!);
        }

        var output = WriteResult(options, result.Value);
        if (output is not null)
        {
            Console.Error.WriteLine($"{output.Code}: {output.Message}");
            return 2;
        }

        return result.Value.IsCompliant ? 0 : 1;
    }

    private static VerifyArchitectureCliOptions? ParseOptions(string[] args)
    {
        var path = Directory.GetCurrentDirectory();
        string? stateRoot = null;
        string? configurationPath = null;
        var json = false;
        var sarif = false;
        string? outputPath = null;
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
                case "--sarif":
                    sarif = true;
                    break;
                case "--output" when index + 1 < args.Length:
                    outputPath = args[++index];
                    break;
                default:
                    return null;
            }
        }

        return json && sarif || outputPath is not null && !sarif
            ? null
            : new VerifyArchitectureCliOptions(path, stateRoot, configurationPath, json, sarif, outputPath);
    }

    private static Problem? WriteResult(VerifyArchitectureCliOptions options, ArchitectureVerification verification)
    {
        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(verification, VerifyArchitectureJsonContext.Default.ArchitectureVerification));
            return null;
        }

        if (options.Sarif)
        {
            return WriteSarif(options.OutputPath, ArchitectureSarifReportBuilder.Build(verification));
        }

        Console.WriteLine($"Architecture verification revision: {verification.GraphRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Baseline: {verification.Baseline.Status} ({verification.Baseline.BaselinePath})");
        foreach (var outcome in verification.Baseline.Findings)
        {
            Console.WriteLine($"{outcome.Status.ToString().ToLowerInvariant()} {outcome.Finding.Kind}: {outcome.Finding.Message}");
        }

        foreach (var exception in verification.Exceptions)
        {
            Console.WriteLine($"exception {exception.State.ToString().ToLowerInvariant()}: {exception.Exception.FindingKey} — {exception.Exception.Author}; expires {exception.Exception.ExpiresAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (verification.IsCompliant)
        {
            Console.WriteLine("Architecture verification passed with no introduced deterministic findings.");
        }
        else
        {
            Console.WriteLine("Architecture verification failed because it introduced deterministic findings.");
        }

        return null;
    }

    private static int WriteError(VerifyArchitectureCliOptions options, Problem problem)
    {
        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new VerifyArchitectureError(problem.Code, problem.Message), VerifyArchitectureJsonContext.Default.VerifyArchitectureError));
            return 2;
        }

        if (options.Sarif)
        {
            var output = WriteSarif(options.OutputPath, ArchitectureSarifReportBuilder.BuildFailure(problem.Code, problem.Message));
            if (output is null)
            {
                return 2;
            }

            Console.Error.WriteLine($"{output.Code}: {output.Message}");
            return 2;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
        return 2;
    }

    private static Problem? WriteSarif(string? outputPath, string sarif)
    {
        try
        {
            if (outputPath is null)
            {
                Console.WriteLine(sarif);
                return null;
            }

            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Problem.Validation("The SARIF output path must include a directory.");
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, sarif + Environment.NewLine);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Problem.Conflict($"Archy could not write the SARIF output: {exception.Message}");
        }
    }

    private sealed record VerifyArchitectureCliOptions(
        string Path,
        string? StateRoot,
        string? ConfigurationPath,
        bool Json,
        bool Sarif,
        string? OutputPath);

    private sealed record VerifyArchitectureError(string Code, string Message);

    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(ArchitectureVerification))]
    [JsonSerializable(typeof(VerifyArchitectureError))]
    private sealed partial class VerifyArchitectureJsonContext : JsonSerializerContext;
}
