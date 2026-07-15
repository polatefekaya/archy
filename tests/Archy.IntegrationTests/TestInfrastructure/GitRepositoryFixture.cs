namespace Archy.IntegrationTests.TestInfrastructure;

internal sealed class GitRepositoryFixture : IAsyncDisposable
{
    private GitRepositoryFixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string HooksDirectory => Path.Combine(Root, ".git", "hooks");

    public static async Task<GitRepositoryFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"archy-git-hooks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await GitProcess.RequireSuccessAsync(root, "init", "--quiet");
        await GitProcess.RequireSuccessAsync(root, "config", "user.email", "archy-tests@example.invalid");
        await GitProcess.RequireSuccessAsync(root, "config", "user.name", "Archy Integration Tests");
        return new GitRepositoryFixture(root);
    }

    public async Task WriteAsync(string repositoryRelativePath, string content)
    {
        var path = Path.Combine(Root, repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    public Task StageAllAsync() => GitProcess.RequireSuccessAsync(Root, "add", "--all");

    public Task CommitAsync(string message, bool bypassHooks = false) => bypassHooks
        ? GitProcess.RequireSuccessAsync(Root, "commit", "--no-verify", "--quiet", "-m", message)
        : GitProcess.RequireSuccessAsync(Root, "commit", "--quiet", "-m", message);

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
