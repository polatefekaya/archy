using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Graph.ReadGraphPage;

public interface IGraphRevisionPageReader
{
    ValueTask<Result<GraphRevisionMetadata?>> ReadMetadataAsync(
        WorkspaceStateLocation location,
        long? revision,
        CancellationToken cancellationToken);

    ValueTask<Result<GraphRevisionPage>> ReadPageAsync(
        WorkspaceStateLocation location,
        GraphRevisionPageQuery query,
        CancellationToken cancellationToken);
}
