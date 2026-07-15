using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.InventorySources;

public interface IRepositorySourceInventory
{
    ValueTask<Result<SourceInventory>> SynchronizeAsync(
        WorkspaceStateLocation location,
        string repositoryRoot,
        ArchyConfiguration configuration,
        CancellationToken cancellationToken);
}
