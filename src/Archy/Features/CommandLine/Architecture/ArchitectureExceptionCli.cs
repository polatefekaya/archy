using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.Architecture;

/// <summary>Owns the explicit, reviewable command for narrowly accepting an architecture finding.</summary>
public static partial class ArchitectureExceptionCli
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
            new AcceptArchitectureExceptionCommand(
                options.Path,
                options.ConfigurationPath,
                options.StateRoot,
                options.FindingKey,
                options.Author,
                options.Reason,
                options.ReviewAtUtc,
                options.ExpiresAtUtc),
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
                ArchitectureExceptionCliJsonContext.Default.AcceptedArchitectureExceptionDecision));
        }
        else
        {
            Console.WriteLine($"Accepted exception '{result.Value.ExceptionId}' through {result.Value.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)}.");
            Console.WriteLine($"Commit {result.Value.Path} so local verification and CI apply the same narrow exception.");
        }

        return 0;
    }

    private static ArchitectureExceptionCliOptions? ParseOptions(string[] args)
    {
        var path = Directory.GetCurrentDirectory();
        string? stateRoot = null;
        string? configurationPath = null;
        string? findingKey = null;
        string? author = null;
        string? reason = null;
        DateTimeOffset? reviewAt = null;
        DateTimeOffset? expiresAt = null;
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
                case "--finding" when index + 1 < args.Length && findingKey is null:
                    findingKey = args[++index];
                    break;
                case "--author" when index + 1 < args.Length && author is null:
                    author = args[++index];
                    break;
                case "--reason" when index + 1 < args.Length && reason is null:
                    reason = args[++index];
                    break;
                case "--review-at" when index + 1 < args.Length && reviewAt is null &&
                                        DateTimeOffset.TryParse(args[++index], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedReview):
                    reviewAt = parsedReview;
                    break;
                case "--expires-at" when index + 1 < args.Length && expiresAt is null &&
                                         DateTimeOffset.TryParse(args[++index], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedExpiry):
                    expiresAt = parsedExpiry;
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return null;
            }
        }

        return string.IsNullOrWhiteSpace(findingKey) ||
               string.IsNullOrWhiteSpace(author) ||
               string.IsNullOrWhiteSpace(reason) ||
               reviewAt is null ||
               expiresAt is null
            ? null
            : new ArchitectureExceptionCliOptions(path, stateRoot, configurationPath, findingKey, author, reason, reviewAt.Value, expiresAt.Value, json);
    }

    private static int WriteUsage()
    {
        Console.Error.WriteLine("Usage: archy exception accept --finding <key> --author <author> --reason <reason> --review-at <ISO-8601> --expires-at <ISO-8601> [--path <path>] [--state-root <path>] [--config <path>] [--json]");
        return 64;
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new ArchitectureExceptionError(problem.Code, problem.Message),
                ArchitectureExceptionCliJsonContext.Default.ArchitectureExceptionError));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private sealed record ArchitectureExceptionCliOptions(
        string Path,
        string? StateRoot,
        string? ConfigurationPath,
        string FindingKey,
        string Author,
        string Reason,
        DateTimeOffset ReviewAtUtc,
        DateTimeOffset ExpiresAtUtc,
        bool Json);

    private sealed record ArchitectureExceptionError(string Code, string Message);

    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(AcceptedArchitectureExceptionDecision))]
    [JsonSerializable(typeof(ArchitectureExceptionError))]
    private sealed partial class ArchitectureExceptionCliJsonContext : JsonSerializerContext;
}
