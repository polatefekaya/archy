using Archy.Features.Architecture.VerificationBaselines;
using Archy.SharedKernel.Primitives;

namespace Archy.UnitTests.Features.Architecture.VerificationBaselines;

public sealed class ArchitectureBaselineComparerTests
{
    private readonly ArchitectureBaselineComparer comparer = new();

    [Fact]
    public void ClassifiesCompatibleBaselineFindingsAsLegacyIntroducedAndResolved()
    {
        var legacy = Finding("finding:legacy");
        var resolved = Finding("finding:resolved");
        var introduced = Finding("finding:introduced");
        var baseline = new ArchitectureVerificationBaseline(
            1,
            "rules:v1",
            7,
            DateTimeOffset.Parse("2026-07-15T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            [legacy, resolved]);

        var result = comparer.Compare("rules:v1", "/repo/archy.baseline.json", [introduced, legacy], baseline);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Equal(ArchitectureBaselineStatus.Compatible, result.Value.Status);
        Assert.Equal(7, result.Value.BaselineGraphRevision);
        Assert.Collection(
            result.Value.Findings,
            outcome => Assert.Equal(("finding:introduced", ArchitectureFindingStatus.Introduced), (outcome.Finding.Key, outcome.Status)),
            outcome => Assert.Equal(("finding:legacy", ArchitectureFindingStatus.Legacy), (outcome.Finding.Key, outcome.Status)),
            outcome => Assert.Equal(("finding:resolved", ArchitectureFindingStatus.Resolved), (outcome.Finding.Key, outcome.Status)));
        Assert.True(result.Value.HasIntroducedFindings);
        Assert.Equal(["finding:introduced", "finding:legacy"], result.Value.CurrentFindings.Select(static finding => finding.Key));
    }

    [Fact]
    public void FailsClosedWhenRuleFingerprintChanged()
    {
        var legacy = Finding("finding:legacy");
        var baseline = new ArchitectureVerificationBaseline(
            1,
            "rules:v1",
            7,
            DateTimeOffset.Parse("2026-07-15T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            [legacy]);

        var result = comparer.Compare("rules:v2", "/repo/archy.baseline.json", [legacy], baseline);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        Assert.Equal(ArchitectureBaselineStatus.Incompatible, result.Value.Status);
        var outcome = Assert.Single(result.Value.Findings);
        Assert.Equal(ArchitectureFindingStatus.Introduced, outcome.Status);
        Assert.Equal("finding:legacy", outcome.Finding.Key);
    }

    [Fact]
    public void TreatsEveryFindingAsIntroducedWhenNoBaselineExists()
    {
        var result = comparer.Compare("rules:v1", "/repo/archy.baseline.json", [Finding("finding:new")], null);

        Assert.True(result.IsSuccess);
        Assert.Equal(ArchitectureBaselineStatus.Missing, result.Value.Status);
        var outcome = Assert.Single(result.Value.Findings);
        Assert.Equal(ArchitectureFindingStatus.Introduced, outcome.Status);
    }

    private static ArchitectureFinding Finding(string key) => new(
        key,
        ArchitectureFindingKind.LayerDependency,
        key,
        [new ArchitectureTarget(ArchitectureTargetKind.Rule, "rule:test")]);
}
