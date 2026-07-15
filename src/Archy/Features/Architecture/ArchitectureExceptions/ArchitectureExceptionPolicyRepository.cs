using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.ArchitectureExceptions;

/// <summary>Persists exception decisions as strict, append-only repository policy rather than hidden local suppression.</summary>
public sealed partial class ArchitectureExceptionPolicyRepository : IArchitectureExceptionPolicyRepository
{
    public const string FileName = "archy.exceptions.json";

    public async ValueTask<Result<ArchitectureExceptionPolicyReadResult>> ReadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(repositoryRoot);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureExceptionPolicyReadResult>(path.Problem!);
        }

        if (!File.Exists(path.Value))
        {
            return ResultFactory.Success(new ArchitectureExceptionPolicyReadResult(
                path.Value,
                new ArchitectureExceptionPolicy(1, [])));
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
            var policy = JsonSerializer.Deserialize(bytes, ArchitectureExceptionJsonContext.Default.ArchitectureExceptionPolicy);
            var validation = ValidatePolicy(policy);
            if (validation is not null)
            {
                return ResultFactory.Failure<ArchitectureExceptionPolicyReadResult>(
                    Problem.Validation($"Architecture exception policy '{path.Value}' is invalid: {validation}"));
            }

            return ResultFactory.Success(new ArchitectureExceptionPolicyReadResult(path.Value, policy!));
        }
        catch (JsonException exception)
        {
            return ResultFactory.Failure<ArchitectureExceptionPolicyReadResult>(
                Problem.Validation($"Architecture exception policy '{path.Value}' is not valid strict JSON: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ArchitectureExceptionPolicyReadResult>(
                Problem.Storage($"Archy could not read architecture exception policy '{path.Value}': {exception.Message}"));
        }
    }

    public async ValueTask<Result<string>> AppendAsync(
        string repositoryRoot,
        ArchitectureExceptionDecision architectureException,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(architectureException);
        var existing = await ReadAsync(repositoryRoot, cancellationToken);
        if (!existing.IsSuccess)
        {
            return ResultFactory.Failure<string>(existing.Problem!);
        }

        var validation = ValidateException(architectureException);
        if (validation is not null)
        {
            return ResultFactory.Failure<string>(Problem.Validation($"Architecture exception cannot be recorded: {validation}"));
        }

        if (existing.Value.Policy.Exceptions.Any(exception =>
                string.Equals(exception.ExceptionId, architectureException.ExceptionId, StringComparison.Ordinal)))
        {
            return ResultFactory.Failure<string>(
                Problem.Conflict($"Architecture exception '{architectureException.ExceptionId}' already exists."));
        }

        var policy = existing.Value.Policy with
        {
            Exceptions = [.. existing.Value.Policy.Exceptions
                .Append(architectureException)
                .OrderBy(static exception => exception.CreatedAtUtc)
                .ThenBy(static exception => exception.ExceptionId, StringComparer.Ordinal)],
        };
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(existing.Value.Path)!,
            $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                policy,
                ArchitectureExceptionJsonContext.Default.ArchitectureExceptionPolicy);
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, existing.Value.Path, overwrite: true);
            return ResultFactory.Success(existing.Value.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<string>(
                Problem.Storage($"Archy could not write architecture exception policy '{existing.Value.Path}': {exception.Message}"));
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A completed policy remains correct; only a later cleanup may be needed for an orphaned temporary file.
            }
        }
    }

    private static Result<string> ResolvePath(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return ResultFactory.Failure<string>(Problem.Validation("Architecture exceptions require a repository root."));
        }

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            if (!Directory.Exists(root))
            {
                return ResultFactory.Failure<string>(Problem.NotFound($"Repository root '{root}' does not exist."));
            }

            return ResultFactory.Success(Path.Combine(root, FileName));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ResultFactory.Failure<string>(Problem.Validation($"Architecture exception policy path is invalid: {exception.Message}"));
        }
    }

    private static string? ValidatePolicy(ArchitectureExceptionPolicy? policy)
    {
        if (policy is null || policy.SchemaVersion != 1 || policy.Exceptions is null ||
            policy.Exceptions.Any(static exception => exception is null) ||
            policy.Exceptions.Select(static exception => exception.ExceptionId).Distinct(StringComparer.Ordinal).Count() != policy.Exceptions.Length)
        {
            return "schema_version and uniquely identified exceptions are required.";
        }

        return policy.Exceptions.Select(ValidateException).FirstOrDefault(static validation => validation is not null);
    }

    private static string? ValidateException(ArchitectureExceptionDecision architectureException)
    {
        if (string.IsNullOrWhiteSpace(architectureException.ExceptionId) ||
            architectureException.ExceptionId.Length > 128 ||
            string.IsNullOrWhiteSpace(architectureException.FindingKey) ||
            architectureException.FindingKey.Length > 4_096 ||
            string.IsNullOrWhiteSpace(architectureException.Author) ||
            architectureException.Author.Length > 1_024 ||
            string.IsNullOrWhiteSpace(architectureException.Reason) ||
            architectureException.Reason.Length > 8_000 ||
            architectureException.CreatedAtUtc == default ||
            architectureException.ReviewAtUtc == default ||
            architectureException.ExpiresAtUtc == default ||
            architectureException.ReviewAtUtc > architectureException.ExpiresAtUtc ||
            architectureException.ExpiresAtUtc <= architectureException.CreatedAtUtc)
        {
            return "each exception requires bounded id, finding_key, author, and reason fields plus review_at_utc and an expiry after creation and review.";
        }

        return null;
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true)]
    [JsonSerializable(typeof(ArchitectureExceptionPolicy))]
    private sealed partial class ArchitectureExceptionJsonContext : JsonSerializerContext;
}
