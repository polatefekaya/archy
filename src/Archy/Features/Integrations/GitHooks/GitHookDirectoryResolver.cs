using System.ComponentModel;
using System.Diagnostics;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.GitHooks;

/// <summary>Uses Git itself so linked worktrees and configured hooks paths retain Git's semantics.</summary>
internal sealed class GitHookDirectoryResolver : IGitHookDirectoryResolver
{
    public async ValueTask<Result<GitHookDirectory>> ResolveAsync(
        LocatedWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var configured = await RunGitAsync(
            workspace.RepositoryRoot,
            ["config", "--path", "--get", "core.hooksPath"],
            cancellationToken);
        if (!configured.IsSuccess)
        {
            return ResultFactory.Failure<GitHookDirectory>(configured.Problem!);
        }

        if (configured.Value.ExitCode == 0)
        {
            var configuredPath = configured.Value.StandardOutput.Trim();
            if (configuredPath.Length == 0)
            {
                return ResultFactory.Failure<GitHookDirectory>(
                    Problem.Validation("Git configured an empty core.hooksPath."));
            }

            return ResolvePath(workspace.RepositoryRoot, configuredPath, usesCustomHooksPath: true);
        }

        if (configured.Value.ExitCode != 1)
        {
            return ResultFactory.Failure<GitHookDirectory>(
                Problem.Storage($"Git could not read core.hooksPath: {configured.Value.StandardError.Trim()}"));
        }

        var defaultPath = await RunGitAsync(
            workspace.RepositoryRoot,
            ["rev-parse", "--git-path", "hooks"],
            cancellationToken);
        if (!defaultPath.IsSuccess)
        {
            return ResultFactory.Failure<GitHookDirectory>(defaultPath.Problem!);
        }

        if (defaultPath.Value.ExitCode != 0 || string.IsNullOrWhiteSpace(defaultPath.Value.StandardOutput))
        {
            return ResultFactory.Failure<GitHookDirectory>(
                Problem.Storage($"Git could not resolve its hook directory: {defaultPath.Value.StandardError.Trim()}"));
        }

        return ResolvePath(workspace.RepositoryRoot, defaultPath.Value.StandardOutput.Trim(), usesCustomHooksPath: false);
    }

    private static Result<GitHookDirectory> ResolvePath(string repositoryRoot, string path, bool usesCustomHooksPath)
    {
        try
        {
            var resolved = Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, repositoryRoot);
            return ResultFactory.Success(new GitHookDirectory(resolved, usesCustomHooksPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ResultFactory.Failure<GitHookDirectory>(
                Problem.Validation($"Git supplied an invalid hook directory: {exception.Message}"));
        }
    }

    private static async ValueTask<Result<GitCommandResult>> RunGitAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = repositoryRoot,
                },
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return ResultFactory.Failure<GitCommandResult>(
                    Problem.Storage("Archy could not start Git to resolve hook paths."));
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return ResultFactory.Success(new GitCommandResult(
                process.ExitCode,
                await standardOutput,
                await standardError));
        }
        catch (Win32Exception exception)
        {
            return ResultFactory.Failure<GitCommandResult>(
                Problem.NotFound($"Git is required to manage local hooks: {exception.Message}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<GitCommandResult>(
                Problem.Storage($"Archy could not resolve the Git hook directory: {exception.Message}"));
        }
    }

    private sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
