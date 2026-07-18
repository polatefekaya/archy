using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;
namespace Archy.Features.Duplicates.IndexEmbeddings;
public sealed record EmbeddingIndexStatusQuery(WorkspaceStateLocation Location, ArchyConfiguration Configuration, string ModelId) : IRequest<Result<EmbeddingIndexStatus>>;
