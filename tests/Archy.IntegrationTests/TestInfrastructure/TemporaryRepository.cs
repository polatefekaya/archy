using Archy.Features.Workspaces.LocateWorkspace;

namespace Archy.IntegrationTests.TestInfrastructure;

internal sealed class TemporaryRepository : IDisposable
{
    private TemporaryRepository(string root)
    {
        Root = root;
        LocateHandler = new LocateWorkspaceHandler(new WorkspaceLocator());
    }

    public string Root { get; }

    public LocateWorkspaceHandler LocateHandler { get; }

    public static TemporaryRepository Create(bool gitMetadataIsFile = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"archy-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var gitMetadataPath = Path.Combine(root, ".git");
        if (gitMetadataIsFile)
        {
            File.WriteAllText(gitMetadataPath, "gitdir: /tmp/archy-worktree-metadata");
        }
        else
        {
            Directory.CreateDirectory(gitMetadataPath);
        }

        return new TemporaryRepository(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
