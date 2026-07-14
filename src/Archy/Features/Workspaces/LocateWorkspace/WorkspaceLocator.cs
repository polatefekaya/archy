using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.LocateWorkspace;

public sealed class WorkspaceLocator : IWorkspaceLocator
{
    public Result<LocatedWorkspace> Locate(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return ResultFactory.Failure<LocatedWorkspace>(
                Problem.Validation("A workspace path is required."));
        }

        var fullPath = Path.GetFullPath(startPath);
        var startingDirectory = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath)
            : fullPath;

        if (startingDirectory is null || !Directory.Exists(startingDirectory))
        {
            return ResultFactory.Failure<LocatedWorkspace>(
                Problem.NotFound($"The path '{fullPath}' does not exist or is not a directory."));
        }

        for (var current = new DirectoryInfo(startingDirectory); current is not null; current = current.Parent)
        {
            var gitMetadataPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitMetadataPath))
            {
                return ResultFactory.Success(
                    new LocatedWorkspace(current.FullName, gitMetadataPath, IsLinkedWorktree: false));
            }

            if (File.Exists(gitMetadataPath))
            {
                return ResultFactory.Success(
                    new LocatedWorkspace(current.FullName, gitMetadataPath, IsLinkedWorktree: true));
            }
        }

        return ResultFactory.Failure<LocatedWorkspace>(
            Problem.NotFound($"No Git repository was found above '{fullPath}'."));
    }
}
