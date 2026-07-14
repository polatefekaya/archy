using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Workspaces;

public sealed class WorkspaceDiscoveryTests
{
    [Fact]
    public async Task LocateResolvesTheRepositoryRootFromANestedDirectory()
    {
        using var fixture = TemporaryRepository.Create();
        var nestedDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, "src", "Feature", "Nested"));

        var result = await fixture.LocateHandler.Handle(
            new LocateWorkspaceCommand(nestedDirectory.FullName),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Root, result.Value.RepositoryRoot);
        Assert.False(result.Value.IsLinkedWorktree);
    }

    [Fact]
    public async Task LocateRecognizesALinkedGitWorktree()
    {
        using var fixture = TemporaryRepository.Create(gitMetadataIsFile: true);

        var result = await fixture.LocateHandler.Handle(
            new LocateWorkspaceCommand(fixture.Root),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsLinkedWorktree);
    }

    [Fact]
    public async Task LocateRejectsAPathOutsideAGitRepository()
    {
        using var directory = TemporaryDirectory.Create("no-repository");

        var result = await new LocateWorkspaceHandler(new WorkspaceLocator())
            .Handle(new LocateWorkspaceCommand(directory.Path), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Problem!.Code);
    }

    [Fact]
    public async Task LocateResolvesTheRepositoryRootFromAFilePath()
    {
        using var fixture = TemporaryRepository.Create();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, "src"));
        var sourceFile = Path.Combine(sourceDirectory.FullName, "Program.cs");
        await File.WriteAllTextAsync(sourceFile, "// fixture");

        var result = await fixture.LocateHandler.Handle(
            new LocateWorkspaceCommand(sourceFile),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Root, result.Value.RepositoryRoot);
    }
}
