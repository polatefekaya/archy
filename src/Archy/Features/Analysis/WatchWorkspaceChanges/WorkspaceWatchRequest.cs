using Archy.Features.Analysis.InventorySources;

namespace Archy.Features.Analysis.WatchWorkspaceChanges;

public sealed record WorkspaceWatchRequest(
    string RepositoryRoot,
    SourceScopePolicy Scope);
