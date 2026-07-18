using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.IndexEmbeddings;

public interface IEmbeddingIndexSourceReader
{
    ValueTask<Result<EmbeddingIndexPlan>> ReadAsync(WorkspaceStateLocation location, string repositoryRoot, ArchyConfiguration configuration, int maxChunks, CancellationToken cancellationToken);
}
