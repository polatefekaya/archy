using Archy.Features.Analysis.InventorySources;
using Archy.Features.Workspaces.LocateWorkspace;

namespace Archy.Features.Integrations.Codex.PostToolChangedPaths;

public sealed record PostToolChangedPathRequest(
    LocatedWorkspace Workspace,
    SourceScopePolicy Scope,
    PostToolKind ToolKind,
    IReadOnlyList<string> HookReportedPaths,
    WorkspacePathSnapshot? BeforeSnapshot,
    WorkspacePathSnapshot? AfterSnapshot,
    IReadOnlyList<string> PreToolDirtyPaths);
