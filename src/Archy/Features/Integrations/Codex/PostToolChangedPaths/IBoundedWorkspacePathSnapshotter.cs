using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

public interface IBoundedWorkspacePathSnapshotter
{
    ValueTask<Result<WorkspacePathSnapshot>> CaptureAsync(
        string repositoryRoot,
        IReadOnlyList<string> repositoryRelativePaths,
        CancellationToken cancellationToken);
}
