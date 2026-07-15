using System.Text.Json;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Integrations.GitHooks;

public sealed class GitHooksCliProcessTests
{
    [Fact]
    public async Task InstallHonorsTheRepositoryConfiguredCoreHooksPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var repository = await GitRepositoryFixture.CreateAsync();
        await GitProcess.RequireSuccessAsync(repository.Root, "config", "core.hooksPath", ".githooks");

        var installed = await ArchyProcess.RunAsync("hooks", "install", "--path", repository.Root, "--json");

        Assert.Equal(0, installed.ExitCode);
        using (var payload = JsonDocument.Parse(installed.StandardOutput))
        {
            Assert.True(payload.RootElement.GetProperty("UsesCustomHooksPath").GetBoolean());
            Assert.Equal(Path.Combine(repository.Root, ".githooks"), payload.RootElement.GetProperty("HooksDirectory").GetString());
        }

        Assert.True(File.Exists(Path.Combine(repository.Root, ".githooks", "pre-commit")));
        Assert.True(File.Exists(Path.Combine(repository.Root, ".githooks", "pre-push")));
        Assert.False(File.Exists(Path.Combine(repository.Root, ".githooks", ".archy-hook-lifecycle.lock")));

        var status = await ArchyProcess.RunAsync("hooks", "status", "--path", repository.Root, "--json");
        Assert.Equal(0, status.ExitCode);
        using (var statusPayload = JsonDocument.Parse(status.StandardOutput))
        {
            Assert.All(
                statusPayload.RootElement.GetProperty("Hooks").EnumerateArray(),
                hook => Assert.True(hook.GetProperty("IsManagedByArchy").GetBoolean()));
        }

        var uninstalled = await ArchyProcess.RunAsync("hooks", "uninstall", "--path", repository.Root, "--json");
        Assert.Equal(0, uninstalled.ExitCode);
        Assert.False(File.Exists(Path.Combine(repository.Root, ".githooks", "pre-commit")));
        Assert.False(File.Exists(Path.Combine(repository.Root, ".githooks", "pre-push")));
    }

    [Fact]
    public async Task InstallBlocksAndAllowsTheCorrectStagedTreeThenRestoresTheOriginalHookOnUninstall()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await using var repository = await GitRepositoryFixture.CreateAsync();
        await repository.WriteAsync(
            "archy.toml",
            """
            schema_version = 1

            [[layers]]
            name = "Inside"
            include = ["src/Inside/**"]
            may_depend_on = []
            """);
        await repository.WriteAsync("src/Inside/Inside.cs", "namespace Sample; public sealed class Inside { }");
        await repository.StageAllAsync();
        await repository.CommitAsync("Initial architecture fixture", bypassHooks: true);

        var originalHook = """
            #!/bin/sh
            printf 'legacy\n' >> .archy-legacy-hook-ran
            exit 0
            """;
        var originalHookPath = Path.Combine(repository.HooksDirectory, "pre-commit");
        await File.WriteAllTextAsync(originalHookPath, originalHook);
        File.SetUnixFileMode(
            originalHookPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);

        var installed = await ArchyProcess.RunAsync("hooks", "install", "--path", repository.Root, "--json");

        Assert.Equal(0, installed.ExitCode);
        Assert.Equal(string.Empty, installed.StandardError);
        using (var installation = JsonDocument.Parse(installed.StandardOutput))
        {
            Assert.All(
                installation.RootElement.GetProperty("Hooks").EnumerateArray(),
                hook => Assert.True(hook.GetProperty("IsManagedByArchy").GetBoolean()));
        }

        var preservedHookPath = Path.Combine(repository.HooksDirectory, "pre-commit.archy-legacy");
        Assert.Equal(originalHook, await File.ReadAllTextAsync(preservedHookPath));
        Assert.StartsWith("#!/bin/sh\n# archy-managed-hook: v1\n", await File.ReadAllTextAsync(originalHookPath), StringComparison.Ordinal);

        var installedAgain = await ArchyProcess.RunAsync("hooks", "install", "--path", repository.Root, "--json");
        Assert.Equal(0, installedAgain.ExitCode);
        Assert.Equal(originalHook, await File.ReadAllTextAsync(preservedHookPath));

        await repository.WriteAsync("docs/allowed.md", "This commit does not alter architecture evidence.");
        await repository.StageAllAsync();
        var allowed = await GitProcess.RunAsync(repository.Root, "commit", "--quiet", "-m", "Allowed staged tree");
        Assert.True(
            allowed.ExitCode == 0,
            $"Allowed hook commit failed.{Environment.NewLine}stdout:{Environment.NewLine}{allowed.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{allowed.StandardError}");
        Assert.True(File.Exists(Path.Combine(repository.Root, ".archy-legacy-hook-ran")));

        var head = await GitProcess.RunAsync(repository.Root, "rev-parse", "HEAD");
        Assert.Equal(0, head.ExitCode);
        var pushed = await LocalProcess.RunAsync(
            Path.Combine(repository.HooksDirectory, "pre-push"),
            repository.Root,
            $"refs/heads/main {head.StandardOutput.Trim()} refs/heads/main {new string('0', 40)}{Environment.NewLine}",
            "origin",
            "https://example.invalid/archy.git");
        Assert.Equal(0, pushed.ExitCode);

        await repository.WriteAsync("src/Outside/Outside.cs", "namespace Sample; public sealed class Outside { }");
        await repository.StageAllAsync();
        var blocked = await GitProcess.RunAsync(repository.Root, "commit", "--quiet", "-m", "Blocked staged tree");
        Assert.Equal(1, blocked.ExitCode);
        var stagedAfterBlock = await GitProcess.RunAsync(repository.Root, "diff", "--cached", "--name-only");
        Assert.Equal(0, stagedAfterBlock.ExitCode);
        Assert.Contains("src/Outside/Outside.cs", stagedAfterBlock.StandardOutput, StringComparison.Ordinal);

        var uninstalled = await ArchyProcess.RunAsync("hooks", "uninstall", "--path", repository.Root, "--json");
        Assert.Equal(0, uninstalled.ExitCode);
        Assert.Equal(string.Empty, uninstalled.StandardError);
        Assert.Equal(originalHook, await File.ReadAllTextAsync(originalHookPath));
        Assert.False(File.Exists(preservedHookPath));
        using var removal = JsonDocument.Parse(uninstalled.StandardOutput);
        Assert.All(
            removal.RootElement.GetProperty("Hooks").EnumerateArray(),
            hook => Assert.False(hook.GetProperty("IsManagedByArchy").GetBoolean()));
    }
}
