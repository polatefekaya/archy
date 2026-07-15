using Archy.Features.Analysis.InventorySources;

namespace Archy.IntegrationTests.Features.Analysis.InventorySources;

public sealed class SourceInventoryMoveReconciliationTests
{
    [Fact]
    public async Task InventoryReconcilesAnUnambiguousContentPreservingMoveAndSettlesTheCache()
    {
        using var fixture = await SourceInventoryFixture.CreateAsync();
        await fixture.WriteRepositoryFileAsync("src/OldName.cs", "namespace Sample; public sealed class Stable { }");
        var initial = await fixture.SynchronizeAsync();
        Assert.True(initial.IsSuccess);

        var oldPath = Path.Combine(fixture.RepositoryRoot, "src", "OldName.cs");
        var newPath = Path.Combine(fixture.RepositoryRoot, "src", "NewName.cs");
        File.Move(oldPath, newPath);

        var moved = await fixture.SynchronizeAsync();

        Assert.True(moved.IsSuccess);
        var change = Assert.Single(moved.Value.Changes);
        Assert.Equal(SourceFileChangeKind.Moved, change.Kind);
        Assert.Equal("src/NewName.cs", change.File.RepositoryRelativePath);
        Assert.NotNull(change.PreviousFile);
        Assert.Equal("src/OldName.cs", change.PreviousFile!.RepositoryRelativePath);
        Assert.Equal(["src/NewName.cs"], moved.Value.ParseCandidates.Select(static file => file.RepositoryRelativePath));

        var settled = await fixture.SynchronizeAsync();
        Assert.True(settled.IsSuccess);
        var settledChange = Assert.Single(settled.Value.Changes);
        Assert.Equal(SourceFileChangeKind.Unchanged, settledChange.Kind);
        Assert.Equal("src/NewName.cs", settledChange.File.RepositoryRelativePath);
    }
}
