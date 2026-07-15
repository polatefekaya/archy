using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Persists the shared baseline as strict JSON beside <c>archy.toml</c>, never in machine-local state.</summary>
public sealed partial class ArchitectureBaselinePolicyRepository : IArchitectureBaselinePolicyRepository
{
    public const string FileName = "archy.baseline.json";

    public async ValueTask<Result<ArchitectureBaselineReadResult>> ReadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var path = ResolvePath(repositoryRoot);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureBaselineReadResult>(path.Problem!);
        }

        if (!File.Exists(path.Value))
        {
            return ResultFactory.Success(new ArchitectureBaselineReadResult(path.Value, null));
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path.Value, cancellationToken);
            var baseline = JsonSerializer.Deserialize(
                bytes,
                ArchitectureBaselineJsonContext.Default.ArchitectureVerificationBaseline);
            var validation = Validate(baseline);
            if (validation is not null)
            {
                return ResultFactory.Failure<ArchitectureBaselineReadResult>(
                    Problem.Validation($"Architecture baseline '{path.Value}' is invalid: {validation}"));
            }

            return ResultFactory.Success(new ArchitectureBaselineReadResult(path.Value, baseline));
        }
        catch (JsonException exception)
        {
            return ResultFactory.Failure<ArchitectureBaselineReadResult>(
                Problem.Validation($"Architecture baseline '{path.Value}' is not valid strict JSON: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<ArchitectureBaselineReadResult>(
                Problem.Storage($"Archy could not read architecture baseline '{path.Value}': {exception.Message}"));
        }
    }

    public async ValueTask<Result<string>> WriteAsync(
        string repositoryRoot,
        ArchitectureVerificationBaseline baseline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var path = ResolvePath(repositoryRoot);
        if (!path.IsSuccess)
        {
            return ResultFactory.Failure<string>(path.Problem!);
        }

        var validation = Validate(baseline);
        if (validation is not null)
        {
            return ResultFactory.Failure<string>(Problem.Validation($"Architecture baseline cannot be written: {validation}"));
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path.Value)!,
            $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                baseline,
                ArchitectureBaselineJsonContext.Default.ArchitectureVerificationBaseline);
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, path.Value, overwrite: true);
            return ResultFactory.Success(path.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<string>(
                Problem.Storage($"Archy could not write architecture baseline '{path.Value}': {exception.Message}"));
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
                // The completed baseline remains valid; a later workspace cleanup can remove an orphaned temporary file.
            }
        }
    }

    private static Result<string> ResolvePath(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return ResultFactory.Failure<string>(Problem.Validation("Architecture baseline requires a repository root."));
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
            return ResultFactory.Failure<string>(Problem.Validation($"Architecture baseline path is invalid: {exception.Message}"));
        }
    }

    private static string? Validate(ArchitectureVerificationBaseline? baseline)
    {
        if (baseline is null || baseline.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(baseline.RuleFingerprint) ||
            baseline.AcceptedGraphRevision < 1 ||
            baseline.AcceptedAtUtc == default ||
            baseline.Findings is null)
        {
            return "schema_version, rule_fingerprint, accepted_graph_revision, accepted_at_utc, and findings are required.";
        }

        if (baseline.Findings.Any(static finding =>
                finding is null ||
                string.IsNullOrWhiteSpace(finding.Key) ||
                !Enum.IsDefined(finding.Kind) ||
                string.IsNullOrWhiteSpace(finding.Message) ||
                finding.Targets is null ||
                finding.Targets.Length == 0 ||
                finding.Targets.Any(static target =>
                    target is null || !Enum.IsDefined(target.Kind) || string.IsNullOrWhiteSpace(target.StableId))) ||
            baseline.Findings.Select(static finding => finding.Key).Distinct(StringComparer.Ordinal).Count() != baseline.Findings.Length ||
            baseline.Findings.Any(finding => finding.Targets
                .Select(static target => (target.Kind, target.StableId))
                .Distinct()
                .Count() != finding.Targets.Length))
        {
            return "findings must have unique keys and non-empty distinct architecture targets.";
        }

        return null;
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UseStringEnumConverter = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true)]
    [JsonSerializable(typeof(ArchitectureVerificationBaseline))]
    private sealed partial class ArchitectureBaselineJsonContext : JsonSerializerContext;
}
