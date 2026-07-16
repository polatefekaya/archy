using Archy.Features.Duplicates.DuplicateFindings;
using Archy.Features.Duplicates.ReadDuplicateObservationPages;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Duplicates.ReadDuplicateObservationPages;

public sealed class DuplicateObservationPageReaderTests
{
    [Fact]
    public async Task PagesObservationHistoryWithoutLoadingSignals()
    {
        using var fixture=WorkspaceStateFixture.Create();var initialized=await fixture.InitializeAsync();Assert.True(initialized.IsSuccess);
        var first=await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation,[GraphRevisionTestBuilder.Node("node:left","l1"),GraphRevisionTestBuilder.Node("node:right","r1")]);var second=await GraphRevisionTestBuilder.CommitAsync(initialized.Value.StateLocation,[GraphRevisionTestBuilder.Node("node:left","l2"),GraphRevisionTestBuilder.Node("node:right","r1")]);
        var store=new DuplicateFindingRepository(TimeProvider.System,new WorkspaceLockManager(TimeProvider.System));var left=new ArchitectureTarget(ArchitectureTargetKind.GraphNode,"node:left");var right=new ArchitectureTarget(ArchitectureTargetKind.GraphNode,"node:right");var observation=await store.RecordObservationAsync(initialized.Value.StateLocation,new DuplicateFindingObservationFact(left,right,first,"v1",.8,"{}",[new DuplicateSignalFact(DuplicateSignalKind.Structural,.8,"{}")] ),CancellationToken.None);Assert.True(observation.IsSuccess);var next=await store.RecordObservationAsync(initialized.Value.StateLocation,new DuplicateFindingObservationFact(left,right,second,"v2",.9,"{}",[new DuplicateSignalFact(DuplicateSignalKind.Structural,.9,"{}")] ),CancellationToken.None);Assert.True(next.IsSuccess);
        var page=await new DuplicateObservationPageReader(new WorkspaceLockManager(TimeProvider.System)).ReadAsync(initialized.Value.StateLocation,observation.Value.FindingId,1,1,CancellationToken.None);
        Assert.True(page.IsSuccess,page.IsSuccess?string.Empty:page.Problem!.Message);Assert.Equal(2,page.Value.TotalCount);Assert.Equal(second,Assert.Single(page.Value.Items).GraphRevision);
    }
}
