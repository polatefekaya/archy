using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Memory.DetermineImportantNodes;

namespace Archy.UnitTests.Features.Memory.DetermineImportantNodes;

public sealed class ImportantNodeEligibilityPolicyTests
{
    [Fact]
    public void DetermineSelectsDirectoriesPublicTargetsAndConfiguredModulesButExcludesGeneratedSources()
    {
        var publicType = new GraphNodeFact("type:clock", "class", "clock", "Clock", "src/Core/Clock.cs", 1, 20, "syntax", 1, "{}", "hash");
        var generated = new GraphNodeFact("type:generated", "class", "generated", "Generated", "src/Generated/Code.g.cs", 1, 2, "syntax", 1, "{}", "hash");
        var snapshot = new GraphRevisionSnapshot(2, [publicType, generated], [],
            [new GraphSymbolFact("symbol:clock", publicType.StableId, "Core.Clock", "public", "class:Clock", "[]", "{}", "hash"), new GraphSymbolFact("symbol:generated", generated.StableId, "Generated", "public", "class:Generated", "[]", "{}", "hash")], []);

        var result = new ImportantNodeEligibilityPolicy().Determine(snapshot, new MemorySelectionConfiguration(false, ["src/Core"], AiSourceSharingMode.Disabled));

        Assert.Contains(result.Included, target => target.Kind == ImportantNodeTargetKind.Directory && target.StableId == "directory:src/Core");
        Assert.Contains(result.Included, target => target.Kind == ImportantNodeTargetKind.Module && target.StableId == "module:src/Core");
        Assert.Contains(result.Included, target => target.Kind == ImportantNodeTargetKind.PublicType && target.StableId == publicType.StableId);
        Assert.DoesNotContain(result.Included, target => target.StableId == generated.StableId);
        Assert.Contains(result.Excluded, exclusion => exclusion.StableId == "symbol:generated" && exclusion.Reason == "generated-source");
    }
}
