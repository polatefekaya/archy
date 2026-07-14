using System.Security.Cryptography;
using System.Text;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Workspaces.InitializeWorkspace;

public sealed class WorkspaceStateLayout : IWorkspaceStateLayout
{
    public Result<WorkspaceStateLocation> Resolve(LocatedWorkspace workspace, string? requestedStateRoot)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var stateRoot = string.IsNullOrWhiteSpace(requestedStateRoot)
            ? GetDefaultStateRoot()
            : requestedStateRoot;

        try
        {
            var canonicalRepositoryRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(workspace.RepositoryRoot));
            var canonicalStateRoot = Path.GetFullPath(stateRoot);
            var workspaceId = CreateWorkspaceId(canonicalRepositoryRoot);
            var stateDirectory = Path.Combine(canonicalStateRoot, workspaceId);

            return ResultFactory.Success(
                new WorkspaceStateLocation(
                    workspaceId,
                    stateDirectory,
                    Path.Combine(stateDirectory, "workspace.json"),
                    Path.Combine(stateDirectory, "locks", "workspace.lock"),
                    Path.Combine(stateDirectory, "archy.db")));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ResultFactory.Failure<WorkspaceStateLocation>(
                Problem.Validation($"The workspace state root is invalid: {exception.Message}"));
        }
    }

    private static string GetDefaultStateRoot()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("Archy could not resolve the current user profile for local state.");
        }

        return Path.Combine(userProfile, ".archy", "workspaces");
    }

    private static string CreateWorkspaceId(string canonicalRepositoryRoot)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRepositoryRoot));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
