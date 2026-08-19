using System.Text.Json;
using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Architecture.VerifyArchitecture;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.ExportSarif;

/// <summary>Creates SARIF 2.1.0 reports from the already evaluated architecture verification result.</summary>
public static class ArchitectureSarifReportBuilder
{
    private const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";
    private const string ProductUri = "https://github.com/polatefekaya/archy";

    public static string Build(ArchitectureVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);

        var exceptionsByFinding = verification.Exceptions
            .GroupBy(static status => status.Exception.FindingKey, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(static status => status.Exception.CreatedAtUtc)
                    .ThenByDescending(static status => status.Exception.ExceptionId, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);
        var locationsByNodeId = verification.SourceLocations
            .GroupBy(static location => location.NodeStableId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.Ordinal);
        var results = verification.Baseline.Findings
            .OrderBy(static outcome => outcome.Finding.Key, StringComparer.Ordinal)
            .Select(outcome => Result(outcome, verification.GraphRevision, exceptionsByFinding, locationsByNodeId))
            .ToArray();

        var run = new ArchitectureSarifRun(
            Tool(),
            results,
            [new ArchitectureSarifInvocation(true, [])]);
        return Serialize(new ArchitectureSarifLog(SchemaUri, "2.1.0", [run]));
    }

    public static string BuildFailure(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var invocation = new ArchitectureSarifInvocation(
            false,
            [new ArchitectureSarifNotification(
                "error",
                new ArchitectureSarifMessage(message),
                new ArchitectureSarifNotificationProperties(code))]);
        var run = new ArchitectureSarifRun(Tool(), [], [invocation]);
        return Serialize(new ArchitectureSarifLog(SchemaUri, "2.1.0", [run]));
    }

    private static ArchitectureSarifTool Tool() => new(
        new ArchitectureSarifDriver(
            "Archy",
            ProductUri,
            [
                new ArchitectureSarifRule(
                    "ARCHY001",
                    new ArchitectureSarifMessage("Every graph node must belong to exactly one configured architecture layer.")),
                new ArchitectureSarifRule(
                    "ARCHY002",
                    new ArchitectureSarifMessage("Hard configured layer dependencies must be respected.")),
                new ArchitectureSarifRule(
                    "ARCHY003",
                    new ArchitectureSarifMessage("Hard configured dependency layers must not contain a cycle.")),
                new ArchitectureSarifRule(
                    "ARCHY004",
                    new ArchitectureSarifMessage("Configured layer rules must be enforceable by at least one eligible graph edge.")),
            ]));

    private static ArchitectureSarifResult Result(
        ArchitectureFindingOutcome outcome,
        long graphRevision,
        Dictionary<string, ArchitectureExceptionStatus> exceptionsByFinding,
        Dictionary<string, ArchitectureVerificationSourceLocation> locationsByNodeId)
    {
        exceptionsByFinding.TryGetValue(outcome.Finding.Key, out var exception);
        return new ArchitectureSarifResult(
            RuleId(outcome.Finding.Kind),
            Level(outcome.Status),
            new ArchitectureSarifMessage(outcome.Finding.Message),
            BaselineState(outcome.Status),
            Locations(outcome.Finding, locationsByNodeId),
            new ArchitectureSarifResultProperties(
                outcome.Finding.Key,
                outcome.Status.ToString(),
                graphRevision,
                exception?.Exception.ExceptionId,
                exception?.State.ToString()));
    }

    private static ArchitectureSarifLocation[]? Locations(
        ArchitectureFinding finding,
        Dictionary<string, ArchitectureVerificationSourceLocation> locationsByNodeId)
    {
        var locations = new List<ArchitectureSarifLocation>();
        var emittedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in finding.Targets.Where(static target => target.Kind == ArchitectureTargetKind.GraphNode))
        {
            if (!emittedNodeIds.Add(target.StableId) ||
                !locationsByNodeId.TryGetValue(target.StableId, out var source) ||
                IsRootedPath(source.RepositoryRelativePath))
            {
                continue;
            }

            var uri = source.RepositoryRelativePath.Replace('\\', '/');
            var region = source.StartLine is null
                ? null
                : new ArchitectureSarifRegion(source.StartLine.Value, source.EndLine);
            locations.Add(new ArchitectureSarifLocation(
                new ArchitectureSarifPhysicalLocation(
                    new ArchitectureSarifArtifactLocation(uri),
                    region)));
        }

        return locations.Count == 0 ? null : [.. locations];
    }

    private static bool IsRootedPath(string path) =>
        Path.IsPathRooted(path) ||
        path.Length >= 3 &&
        char.IsAsciiLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '/' || path[2] == '\\');

    private static string RuleId(ArchitectureFindingKind kind) => kind switch
    {
        ArchitectureFindingKind.LayerCoverage => "ARCHY001",
        ArchitectureFindingKind.LayerDependency => "ARCHY002",
        ArchitectureFindingKind.DependencyCycle => "ARCHY003",
        ArchitectureFindingKind.EnforcementUnavailable => "ARCHY004",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported architecture finding kind."),
    };

    private static string Level(ArchitectureFindingStatus status) => status switch
    {
        ArchitectureFindingStatus.Introduced => "error",
        ArchitectureFindingStatus.Legacy => "warning",
        ArchitectureFindingStatus.Resolved => "note",
        ArchitectureFindingStatus.Excepted => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported architecture finding status."),
    };

    private static string BaselineState(ArchitectureFindingStatus status) => status switch
    {
        ArchitectureFindingStatus.Introduced => "new",
        ArchitectureFindingStatus.Legacy or ArchitectureFindingStatus.Excepted => "unchanged",
        ArchitectureFindingStatus.Resolved => "absent",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported architecture finding status."),
    };

    private static string Serialize(ArchitectureSarifLog log) =>
        JsonSerializer.Serialize(log, ArchitectureSarifJsonContext.Default.ArchitectureSarifLog);
}
