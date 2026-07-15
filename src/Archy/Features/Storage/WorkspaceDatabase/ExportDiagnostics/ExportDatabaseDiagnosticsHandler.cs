using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Storage.WorkspaceDatabase.ExportDiagnostics;

public sealed class ExportDatabaseDiagnosticsHandler(WorkspaceDatabaseDiagnosticsExporter diagnosticsExporter)
    : IRequestHandler<ExportDatabaseDiagnosticsCommand, Result<DatabaseDiagnosticsExport>>
{
    public ValueTask<Result<DatabaseDiagnosticsExport>> Handle(
        ExportDatabaseDiagnosticsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return diagnosticsExporter.ExportAsync(command.Location, cancellationToken);
    }
}
