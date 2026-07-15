using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.UnitTests.Features.Architecture.VerificationBaselines;

public sealed class ArchitectureRuleFingerprintTests
{
    [Fact]
    public void IgnoresSemanticallyIrrelevantOrderingOfRulesPatternsAndHardEdgeKinds()
    {
        var first = ArchitectureRuleFingerprint.Calculate(
            [
                new LayerRuleConfiguration("Application", ["src/Application/**", "src/UseCases/**"], ["Domain", "Infrastructure"]),
                new LayerRuleConfiguration("Domain", ["src/Domain/**"], []),
            ],
            new ArchitectureEnforcementConfiguration(["references", "calls"]));
        var reordered = ArchitectureRuleFingerprint.Calculate(
            [
                new LayerRuleConfiguration("Domain", ["src/Domain/**"], []),
                new LayerRuleConfiguration("Application", ["src/UseCases/**", "src/Application/**"], ["Infrastructure", "Domain"]),
            ],
            new ArchitectureEnforcementConfiguration(["calls", "references"]));

        Assert.Equal(first, reordered);
    }

    [Fact]
    public void ChangesWhenOneDependencyDirectionChanges()
    {
        var first = ArchitectureRuleFingerprint.Calculate(
            [new LayerRuleConfiguration("Application", ["src/Application/**"], ["Domain"])],
            new ArchitectureEnforcementConfiguration(["calls"]));
        var changed = ArchitectureRuleFingerprint.Calculate(
            [new LayerRuleConfiguration("Application", ["src/Application/**"], [])],
            new ArchitectureEnforcementConfiguration(["calls"]));

        Assert.NotEqual(first, changed);
    }
}
