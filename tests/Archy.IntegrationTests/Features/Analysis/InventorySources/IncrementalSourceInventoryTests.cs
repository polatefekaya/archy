using Archy.Features.Analysis.InventorySources;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.InventorySources;

public sealed class IncrementalSourceInventoryTests
{
    [Fact]
    public async Task IncrementalInventoryClassifiesContentChangesAndRemovesDeletedCacheEntries()
    {
        using var fixture = await SourceInventoryFixture.CreateAsync();
        await fixture.WriteRepositoryFileAsync("src/App.cs", "namespace Sample; public sealed class App { }");
        await fixture.WriteRepositoryFileAsync("src/Old.cs", "namespace Sample; public sealed class Old { }");
        await fixture.WriteRepositoryFileAsync("src/README.md", "stable");

        var initial = await fixture.SynchronizeAsync();
        Assert.True(initial.IsSuccess);
        Assert.All(initial.Value.Changes, static change => Assert.Equal(SourceFileChangeKind.Added, change.Kind));

        var unchanged = await fixture.SynchronizeAsync();
        Assert.True(unchanged.IsSuccess);
        Assert.All(unchanged.Value.Changes, static change => Assert.Equal(SourceFileChangeKind.Unchanged, change.Kind));
        Assert.Empty(unchanged.Value.ParseCandidates);

        await fixture.WriteRepositoryFileAsync("src/App.cs", "namespace Sample; public sealed class Bpp { }");
        fixture.DeleteRepositoryFile("src/Old.cs");
        await fixture.WriteRepositoryFileAsync("src/New.cs", "namespace Sample; public sealed class New { }");

        var changed = await fixture.SynchronizeAsync();

        Assert.True(changed.IsSuccess);
        Assert.Equal(
            [
                ("src/App.cs", SourceFileChangeKind.Changed),
                ("src/New.cs", SourceFileChangeKind.Added),
                ("src/Old.cs", SourceFileChangeKind.Deleted),
                ("src/README.md", SourceFileChangeKind.Unchanged),
            ],
            changed.Value.Changes.Select(static change => (change.File.RepositoryRelativePath, change.Kind)));
        Assert.Equal(
            ["src/App.cs", "src/New.cs"],
            changed.Value.ParseCandidates.Select(static file => file.RepositoryRelativePath));
        Assert.Equal(3L, await SqliteAssertions.CountAsync(fixture.Location.DatabasePath, "source_inventory_files"));

        var settled = await fixture.SynchronizeAsync();
        Assert.True(settled.IsSuccess);
        Assert.DoesNotContain(
            settled.Value.Changes,
            static change => change.File.RepositoryRelativePath == "src/Old.cs");
        Assert.All(settled.Value.Changes, static change => Assert.Equal(SourceFileChangeKind.Unchanged, change.Kind));
    }
}
