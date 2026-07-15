using Archy.Features.Architecture.VerificationBaselines;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Architecture.VerificationBaselines;

public sealed class ArchitectureBaselinePolicyRepositoryTests
{
    [Fact]
    public async Task WritesAndReadsTheStrictRepositoryOwnedBaselineArtifact()
    {
        using var repository = TemporaryRepository.Create();
        var policyRepository = new ArchitectureBaselinePolicyRepository();
        var baseline = Baseline("finding:legacy");

        var written = await policyRepository.WriteAsync(repository.Root, baseline, CancellationToken.None);
        var read = await policyRepository.ReadAsync(repository.Root, CancellationToken.None);

        Assert.True(written.IsSuccess, written.IsSuccess ? string.Empty : written.Problem!.Message);
        Assert.Equal(Path.Combine(repository.Root, ArchitectureBaselinePolicyRepository.FileName), written.Value);
        Assert.True(read.IsSuccess, read.IsSuccess ? string.Empty : read.Problem!.Message);
        Assert.NotNull(read.Value.Baseline);
        Assert.Equal(baseline.SchemaVersion, read.Value.Baseline.SchemaVersion);
        Assert.Equal(baseline.RuleFingerprint, read.Value.Baseline.RuleFingerprint);
        Assert.Equal(baseline.AcceptedGraphRevision, read.Value.Baseline.AcceptedGraphRevision);
        Assert.Equal(baseline.AcceptedAtUtc, read.Value.Baseline.AcceptedAtUtc);
        var expectedFinding = Assert.Single(baseline.Findings);
        var actualFinding = Assert.Single(read.Value.Baseline.Findings);
        Assert.Equal(expectedFinding.Key, actualFinding.Key);
        Assert.Equal(expectedFinding.Kind, actualFinding.Kind);
        Assert.Equal(expectedFinding.Message, actualFinding.Message);
        Assert.Equal(
            expectedFinding.Targets.Select(static target => (target.Kind, target.StableId)),
            actualFinding.Targets.Select(static target => (target.Kind, target.StableId)));
        var document = await File.ReadAllTextAsync(written.Value);
        Assert.Contains("\"schemaVersion\": 1", document, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"LayerDependency\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsUnknownPropertiesRatherThanSilentlyIgnoringBaselinePolicy()
    {
        using var repository = TemporaryRepository.Create();
        var path = Path.Combine(repository.Root, ArchitectureBaselinePolicyRepository.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "ruleFingerprint": "rules:v1",
              "acceptedGraphRevision": 1,
              "acceptedAtUtc": "2026-07-15T12:00:00+00:00",
              "findings": [],
              "unrecognizedSuppression": true
            }
            """);
        var policyRepository = new ArchitectureBaselinePolicyRepository();

        var result = await policyRepository.ReadAsync(repository.Root, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
        Assert.Contains("strict JSON", result.Problem.Message, StringComparison.Ordinal);
    }

    private static ArchitectureVerificationBaseline Baseline(string key) => new(
        1,
        "rules:v1",
        1,
        DateTimeOffset.Parse("2026-07-15T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
        [new ArchitectureFinding(
            key,
            ArchitectureFindingKind.LayerDependency,
            "A stable fixture finding.",
            [new ArchitectureTarget(ArchitectureTargetKind.Rule, "fixture-rule")])]);
}
