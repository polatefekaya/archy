using Archy.Features.Memory.AccumulateTouchedNodes;
using Archy.Features.Memory.DetermineImportantNodes;

namespace Archy.UnitTests.Features.Memory.AccumulateTouchedNodes;

public sealed class TouchedNodeSessionAccumulatorTests
{
    [Fact]
    public void PrepareAndAcknowledgePreservesCoTouchedOrderingAndDoesNotLoseUnacknowledgedWork()
    {
        var policy = new TouchedNodeSessionAccumulator(TimeProvider.System);
        var eligibility = new ImportantNodeEligibility(4,
        [new ImportantNodeTarget(ImportantNodeTargetKind.PublicType, "type:a", "A", ["src/A.cs"], [ImportantNodeEligibilityReason.PublicTypeDeclaration]), new ImportantNodeTarget(ImportantNodeTargetKind.PublicType, "type:b", "B", ["src/B.cs"], [ImportantNodeEligibilityReason.PublicTypeDeclaration])], []);

        Assert.Equal(1, policy.Record(new TouchedNodeRecord("session", eligibility, ["src/B.cs"])));
        Assert.Equal(1, policy.Record(new TouchedNodeRecord("session", eligibility, ["src/A.cs"])));
        var candidate = policy.PrepareFlush("session", "batch", "idle", "{}")!;
        var retried = policy.PrepareFlush("session", "batch", "different", "{}")!;

        Assert.Same(candidate, retried);
        Assert.Equal(["type:b", "type:a"], candidate.Batch.Members.Select(static member => member.TargetStableId));
        Assert.Equal([2], candidate.Batch.Members[0].CoTouchedMemberOrdinals);
        policy.AcknowledgePersisted("session", "batch");
        Assert.Null(policy.PrepareFlush("session", "next", "idle", "{}"));
    }
}
