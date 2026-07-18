using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Duplicates.IndexEmbeddings;
public sealed record IndexEmbeddingsCommand(WorkspaceStateLocation Location, string RepositoryRoot, ArchyConfiguration Configuration, string ModelId, int MaxChunks, bool DryRun) : IRequest<Result<EmbeddingIndexResult>>;
