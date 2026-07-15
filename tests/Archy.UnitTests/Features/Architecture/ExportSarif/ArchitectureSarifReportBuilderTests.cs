using System.Text.Json;
using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.ExportSarif;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Architecture.VerifyArchitecture;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Architecture.ExportSarif;

public sealed class ArchitectureSarifReportBuilderTests
{
    [Fact]
    public void BuildsAStableSarifReportFromTheAlreadyClassifiedVerificationResult()
    {
        using var report = JsonDocument.Parse(ArchitectureSarifReportBuilder.Build(Verification()));

        var root = report.RootElement;
        Assert.Equal("https://json.schemastore.org/sarif-2.1.0.json", root.GetProperty("$schema").GetString());
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        var run = Assert.Single(root.GetProperty("runs").EnumerateArray());
        Assert.Equal("Archy", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        Assert.Collection(
            run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray(),
            rule => Assert.Equal("ARCHY001", rule.GetProperty("id").GetString()),
            rule => Assert.Equal("ARCHY002", rule.GetProperty("id").GetString()),
            rule => Assert.Equal("ARCHY003", rule.GetProperty("id").GetString()));

        var results = run.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(4, results.Length);

        var introduced = Result(results, "coverage:introduced");
        Assert.Equal("ARCHY001", introduced.GetProperty("ruleId").GetString());
        Assert.Equal("error", introduced.GetProperty("level").GetString());
        Assert.Equal("new", introduced.GetProperty("baselineState").GetString());
        Assert.Equal("Introduced", introduced.GetProperty("properties").GetProperty("findingStatus").GetString());
        Assert.Equal(42L, introduced.GetProperty("properties").GetProperty("graphRevision").GetInt64());
        var location = Assert.Single(introduced.GetProperty("locations").EnumerateArray());
        Assert.Equal(
            "src/Unassigned.cs",
            location.GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(
            7,
            location.GetProperty("physicalLocation").GetProperty("region").GetProperty("startLine").GetInt32());

        var legacy = Result(results, "dependency:legacy");
        Assert.Equal("ARCHY002", legacy.GetProperty("ruleId").GetString());
        Assert.Equal("warning", legacy.GetProperty("level").GetString());
        Assert.Equal("unchanged", legacy.GetProperty("baselineState").GetString());

        var excepted = Result(results, "cycle:excepted");
        Assert.Equal("ARCHY003", excepted.GetProperty("ruleId").GetString());
        Assert.Equal("none", excepted.GetProperty("level").GetString());
        Assert.Equal("unchanged", excepted.GetProperty("baselineState").GetString());
        Assert.Equal("exception:cycle", excepted.GetProperty("properties").GetProperty("exceptionId").GetString());
        Assert.Equal("Active", excepted.GetProperty("properties").GetProperty("exceptionState").GetString());

        var resolved = Result(results, "coverage:resolved");
        Assert.Equal("note", resolved.GetProperty("level").GetString());
        Assert.Equal("absent", resolved.GetProperty("baselineState").GetString());
        Assert.False(resolved.TryGetProperty("locations", out _));
    }

    [Fact]
    public void BuildsAnUnsuccessfulSarifInvocationWhenVerificationCannotRun()
    {
        using var report = JsonDocument.Parse(ArchitectureSarifReportBuilder.BuildFailure("conflict", "The graph is unavailable."));

        var invocation = Assert.Single(report.RootElement
            .GetProperty("runs")
            .EnumerateArray()
            .Single()
            .GetProperty("invocations")
            .EnumerateArray());
        Assert.False(invocation.GetProperty("executionSuccessful").GetBoolean());
        var notification = Assert.Single(invocation.GetProperty("toolExecutionNotifications").EnumerateArray());
        Assert.Equal("error", notification.GetProperty("level").GetString());
        Assert.Equal("The graph is unavailable.", notification.GetProperty("message").GetProperty("text").GetString());
        Assert.Equal("conflict", notification.GetProperty("properties").GetProperty("problemCode").GetString());
    }

    private static JsonElement Result(JsonElement[] results, string key) => Assert.Single(
        results,
        result => result.GetProperty("properties").GetProperty("findingKey").GetString() == key);

    private static ArchitectureVerification Verification()
    {
        var createdAt = DateTimeOffset.Parse(
            "2026-07-15T12:00:00+00:00",
            System.Globalization.CultureInfo.InvariantCulture);
        var cycleException = new ArchitectureExceptionDecision(
            "exception:cycle",
            "cycle:excepted",
            "fixture-author",
            "The test needs one reviewed exception.",
            createdAt.AddDays(1),
            createdAt.AddDays(7),
            createdAt);
        return new ArchitectureVerification(
            42,
            false,
            "rules:fixture",
            new LayerDependencyEvaluation([], [], [], []),
            new ArchitectureBaselineComparison(
                ArchitectureBaselineStatus.Compatible,
                "/repo/archy.baseline.json",
                41,
                [
                    new ArchitectureFindingOutcome(
                        Finding("coverage:introduced", ArchitectureFindingKind.LayerCoverage, "node:coverage"),
                        ArchitectureFindingStatus.Introduced),
                    new ArchitectureFindingOutcome(
                        Finding("dependency:legacy", ArchitectureFindingKind.LayerDependency, "node:legacy"),
                        ArchitectureFindingStatus.Legacy),
                    new ArchitectureFindingOutcome(
                        Finding("cycle:excepted", ArchitectureFindingKind.DependencyCycle, "node:cycle"),
                        ArchitectureFindingStatus.Excepted),
                    new ArchitectureFindingOutcome(
                        Finding("coverage:resolved", ArchitectureFindingKind.LayerCoverage, "node:resolved"),
                        ArchitectureFindingStatus.Resolved),
                ]),
            [new ArchitectureExceptionStatus(cycleException, ArchitectureExceptionState.Active, true)],
            [
                new ArchitectureVerificationSourceLocation("node:coverage", "src\\Unassigned.cs", 7, 9),
                new ArchitectureVerificationSourceLocation("node:legacy", "src/Legacy.cs", 12, 14),
                new ArchitectureVerificationSourceLocation("node:cycle", "src/Cycle.cs", 20, 23),
            ]);
    }

    private static ArchitectureFinding Finding(
        string key,
        ArchitectureFindingKind kind,
        string nodeId) => new(
        key,
        kind,
        $"Message for {key}.",
        [new ArchitectureTarget(ArchitectureTargetKind.GraphNode, nodeId)]);
}
