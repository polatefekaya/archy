using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Duplicates.SelectEmbeddingChunks;
using Archy.Features.Memory.ModelProviders.Contracts;
using Archy.Features.Workspaces.InitializeWorkspace;

namespace Archy.Features.Duplicates.IndexEmbeddings;

public sealed record EmbeddingIndexRequest(
    WorkspaceStateLocation Location,
    long GraphRevision,
    ArchyConfiguration Configuration,
    string ModelId,
    IReadOnlyList<EmbeddingChunk> Chunks,
    IModelProvider Provider);
