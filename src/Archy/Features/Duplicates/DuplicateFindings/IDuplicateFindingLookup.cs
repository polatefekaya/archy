using Archy.Features.Workspaces.InitializeWorkspace;using Archy.SharedKernel.Primitives;
namespace Archy.Features.Duplicates.DuplicateFindings;
public interface IDuplicateFindingLookup { ValueTask<Result<IReadOnlyList<string>>> FindIdsByTargetAsync(WorkspaceStateLocation location,string targetStableId,CancellationToken cancellationToken); }
