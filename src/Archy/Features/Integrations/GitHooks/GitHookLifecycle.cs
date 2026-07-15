using System.Text;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.GitHooks;

/// <summary>Owns idempotent, reversible installation of Archy's preserved local delivery-boundary hooks.</summary>
internal sealed class GitHookLifecycle(
    IWorkspaceLocator workspaceLocator,
    IGitHookDirectoryResolver directoryResolver)
    : IGitHookLifecycle
{
    private static readonly GitHookKind[] SupportedHooks = [GitHookKind.PreCommit, GitHookKind.PrePush];

    public async ValueTask<Result<GitHookInstallation>> InstallAsync(string startPath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return ResultFactory.Failure<GitHookInstallation>(
                Problem.Conflict("Local Git hook installation currently requires a POSIX shell; macOS is the supported initial platform."));
        }

        var context = await ResolveContextAsync(startPath, cancellationToken);
        if (!context.IsSuccess)
        {
            return ResultFactory.Failure<GitHookInstallation>(context.Problem!);
        }

        var invocation = ArchyHookInvocation.Resolve();
        if (!invocation.IsSuccess)
        {
            return ResultFactory.Failure<GitHookInstallation>(invocation.Problem!);
        }

        try
        {
            Directory.CreateDirectory(context.Value.Directory.Path);
            var lifecycleLockPath = LifecycleLockPath(context.Value.Directory.Path);
            try
            {
                using var lifecycleLock = OpenLifecycleLock(lifecycleLockPath);
                var snapshots = CaptureSnapshots(context.Value.Directory.Path);
                try
                {
                    foreach (var hook in SupportedHooks)
                    {
                        InstallHook(context.Value.Directory.Path, hook, invocation.Value);
                    }

                    return ResultFactory.Success(Inspect(context.Value));
                }
                catch
                {
                    RestoreSnapshots(snapshots);
                    throw;
                }
            }
            finally
            {
                DeleteLifecycleLock(lifecycleLockPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ResultFactory.Failure<GitHookInstallation>(
                Problem.Storage($"Archy could not install Git hooks: {exception.Message}"));
        }
    }

    public async ValueTask<Result<GitHookInstallation>> UninstallAsync(string startPath, CancellationToken cancellationToken)
    {
        var context = await ResolveContextAsync(startPath, cancellationToken);
        if (!context.IsSuccess)
        {
            return ResultFactory.Failure<GitHookInstallation>(context.Problem!);
        }

        if (!Directory.Exists(context.Value.Directory.Path))
        {
            return ResultFactory.Success(Inspect(context.Value));
        }

        try
        {
            var lifecycleLockPath = LifecycleLockPath(context.Value.Directory.Path);
            try
            {
                using var lifecycleLock = OpenLifecycleLock(lifecycleLockPath);
                var snapshots = CaptureSnapshots(context.Value.Directory.Path);
                try
                {
                    foreach (var hook in SupportedHooks)
                    {
                        UninstallHook(context.Value.Directory.Path, hook);
                    }

                    return ResultFactory.Success(Inspect(context.Value));
                }
                catch
                {
                    RestoreSnapshots(snapshots);
                    throw;
                }
            }
            finally
            {
                DeleteLifecycleLock(lifecycleLockPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ResultFactory.Failure<GitHookInstallation>(
                Problem.Storage($"Archy could not uninstall Git hooks: {exception.Message}"));
        }
    }

    public async ValueTask<Result<GitHookInstallation>> InspectAsync(string startPath, CancellationToken cancellationToken)
    {
        try
        {
            var context = await ResolveContextAsync(startPath, cancellationToken);
            return context.IsSuccess
                ? ResultFactory.Success(Inspect(context.Value))
                : ResultFactory.Failure<GitHookInstallation>(context.Problem!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ResultFactory.Failure<GitHookInstallation>(
                Problem.Storage($"Archy could not inspect Git hooks: {exception.Message}"));
        }
    }

    private async ValueTask<Result<GitHookContext>> ResolveContextAsync(string startPath, CancellationToken cancellationToken)
    {
        var workspace = workspaceLocator.Locate(startPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<GitHookContext>(workspace.Problem!);
        }

        var directory = await directoryResolver.ResolveAsync(workspace.Value, cancellationToken);
        return directory.IsSuccess
            ? ResultFactory.Success(new GitHookContext(workspace.Value, directory.Value))
            : ResultFactory.Failure<GitHookContext>(directory.Problem!);
    }

    private static string LifecycleLockPath(string hooksDirectory) =>
        Path.Combine(hooksDirectory, ".archy-hook-lifecycle.lock");

    private static FileStream OpenLifecycleLock(string path) =>
        new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void DeleteLifecycleLock(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The lifecycle result is already known; a stale lock artifact is harmless and is retried next operation.
        }
    }

    private static GitHookFileSnapshot[] CaptureSnapshots(string hooksDirectory) =>
        [.. SupportedHooks.SelectMany(hook => new[]
        {
            GitHookFileSnapshot.Capture(HookPath(hooksDirectory, hook)),
            GitHookFileSnapshot.Capture(LegacyPath(hooksDirectory, hook)),
        })];

    private static void RestoreSnapshots(IEnumerable<GitHookFileSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            snapshot.Restore();
        }
    }

    private static void InstallHook(string hooksDirectory, GitHookKind hook, ArchyHookInvocation invocation)
    {
        var hookPath = HookPath(hooksDirectory, hook);
        var legacyPath = LegacyPath(hooksDirectory, hook);
        if (File.Exists(hookPath) && !GitHookScriptFactory.IsManaged(File.ReadAllText(hookPath)))
        {
            if (File.Exists(legacyPath))
            {
                throw new IOException($"Cannot preserve '{hookPath}' because '{legacyPath}' already exists.");
            }

            File.Move(hookPath, legacyPath);
        }
        else if (!File.Exists(hookPath) && File.Exists(legacyPath))
        {
            throw new IOException($"Cannot install '{hookPath}' because an unchained preserved hook already exists at '{legacyPath}'.");
        }

        WriteAtomically(hookPath, GitHookScriptFactory.Create(hook, invocation));
        SetExecutable(hookPath);
    }

    private static void UninstallHook(string hooksDirectory, GitHookKind hook)
    {
        var hookPath = HookPath(hooksDirectory, hook);
        var legacyPath = LegacyPath(hooksDirectory, hook);
        if (File.Exists(hookPath) && GitHookScriptFactory.IsManaged(File.ReadAllText(hookPath)))
        {
            File.Delete(hookPath);
            if (File.Exists(legacyPath))
            {
                File.Move(legacyPath, hookPath);
            }

            return;
        }

        if (File.Exists(legacyPath))
        {
            throw new IOException($"Cannot uninstall '{hookPath}' because its preserved hook exists without an Archy-managed wrapper.");
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.archy-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void SetExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
        }
    }

    private static GitHookInstallation Inspect(GitHookContext context) => new(
        context.Workspace.RepositoryRoot,
        context.Directory.Path,
        context.Directory.UsesCustomHooksPath,
        [.. SupportedHooks.Select(hook =>
        {
            var hookPath = HookPath(context.Directory.Path, hook);
            return new GitHookState(
                hook,
                hookPath,
                File.Exists(hookPath) && GitHookScriptFactory.IsManaged(File.ReadAllText(hookPath)),
                File.Exists(LegacyPath(context.Directory.Path, hook)));
        })]);

    private static string HookPath(string hooksDirectory, GitHookKind hook) =>
        Path.Combine(hooksDirectory, GitHookScriptFactory.FileName(hook));

    private static string LegacyPath(string hooksDirectory, GitHookKind hook) =>
        Path.Combine(hooksDirectory, GitHookScriptFactory.LegacyFileName(hook));

    private sealed record GitHookContext(LocatedWorkspace Workspace, GitHookDirectory Directory);
}
