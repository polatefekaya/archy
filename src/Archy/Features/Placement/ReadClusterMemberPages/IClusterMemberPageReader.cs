using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Placement.ReadClusterMemberPages;

public interface IClusterMemberPageReader { ValueTask<Result<ClusterMemberPage>> ReadAsync(WorkspaceStateLocation location,string revisionId,string clusterId,int offset,int limit,CancellationToken cancellationToken); }
