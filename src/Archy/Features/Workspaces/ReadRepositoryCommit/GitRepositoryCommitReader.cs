using System.ComponentModel;
using System.Diagnostics;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.ReadRepositoryCommit;

public sealed class GitRepositoryCommitReader : IRepositoryCommitReader, IRepositoryProvenanceReader
{
    public async ValueTask<Result<string?>> ReadHeadAsync(
        LocatedWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workspace.RepositoryRoot,
                },
            };
            process.StartInfo.ArgumentList.Add("rev-parse");
            process.StartInfo.ArgumentList.Add("--verify");
            process.StartInfo.ArgumentList.Add("HEAD");
            if (!process.Start())
            {
                return ResultFactory.Failure<string?>(
                    Problem.Storage("Archy could not start Git to read the repository commit."));
            }

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var commit = (await output).Trim();
            _ = await error;
            if (process.ExitCode != 0 || commit.Length == 0)
            {
                return ResultFactory.Success<string?>(null);
            }

            return IsCommitHash(commit)
                ? ResultFactory.Success<string?>(commit.ToLowerInvariant())
                : ResultFactory.Failure<string?>(
                    Problem.Storage("Git returned an invalid repository commit identifier."));
        }
        catch (Win32Exception)
        {
            return ResultFactory.Success<string?>(null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<string?>(
                Problem.Storage($"Archy could not read the repository commit: {exception.Message}"));
        }
    }

    public async ValueTask<Result<RepositoryProvenance>> ReadAsync(
        LocatedWorkspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var head = await ReadHeadAsync(workspace, cancellationToken);
        if (!head.IsSuccess)
        {
            return ResultFactory.Failure<RepositoryProvenance>(head.Problem!);
        }

        var status = await ReadStatusAsync(workspace.RepositoryRoot, cancellationToken);
        if (status is null)
        {
            return ResultFactory.Success(new RepositoryProvenance(
                head.Value,
                RepositoryWorktreeState.Unavailable,
                []));
        }

        var changedPaths = ParsePorcelainPaths(status)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        return ResultFactory.Success(new RepositoryProvenance(
            head.Value,
            changedPaths.Length == 0 ? RepositoryWorktreeState.Clean : RepositoryWorktreeState.Dirty,
            changedPaths));
    }

    private static async Task<string?> ReadStatusAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = repositoryRoot,
                },
            };
            process.StartInfo.ArgumentList.Add("status");
            process.StartInfo.ArgumentList.Add("--porcelain=v1");
            process.StartInfo.ArgumentList.Add("-z");
            process.StartInfo.ArgumentList.Add("--untracked-files=all");
            if (!process.Start())
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            _ = await error;
            return process.ExitCode == 0 ? await output : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<string> ParsePorcelainPaths(string porcelain)
    {
        if (string.IsNullOrEmpty(porcelain))
        {
            return [];
        }

        var tokens = porcelain.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var entry = tokens[index];
            if (entry.Length < 4)
            {
                continue;
            }

            var status = entry[..2];
            var path = entry[3..];
            if (path.Length > 0)
            {
                paths.Add(path);
            }

            if ((status[0] is 'R' or 'C' || status[1] is 'R' or 'C') && index + 1 < tokens.Length)
            {
                paths.Add(tokens[++index]);
            }
        }

        return paths;
    }

    private static bool IsCommitHash(string value) =>
        value.Length is 40 or 64 && value.All(static character => char.IsAsciiHexDigit(character));
}
