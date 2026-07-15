using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.ExportDiagnostics;

public sealed record ExportDatabaseDiagnosticsCommand(WorkspaceStateLocation Location)
    : IRequest<Result<DatabaseDiagnosticsExport>>;
