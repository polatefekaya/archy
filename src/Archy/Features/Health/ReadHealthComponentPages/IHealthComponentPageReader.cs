using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Health.ReadHealthComponentPages;

public interface IHealthComponentPageReader { ValueTask<Result<HealthComponentPage>> ReadAsync(WorkspaceStateLocation location,string snapshotId,int offset,int limit,CancellationToken cancellationToken); }
