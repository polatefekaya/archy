using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Archy.Features.Storage.WorkspaceDatabase.Backup;
using Archy.Features.Storage.WorkspaceDatabase.Check;
using Archy.Features.Storage.WorkspaceDatabase.ExportDiagnostics;
using Archy.Features.Storage.WorkspaceDatabase.Integrity;
using Archy.Features.Storage.WorkspaceDatabase.Restore;
using Archy.Features.Storage.WorkspaceDatabase.Vacuum;
using Archy.Features.Workspaces.ResolveWorkspaceState;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.CommandLine.WorkspaceDatabase;

public static partial class WorkspaceDatabaseCli
{
    public static async Task<int> RunAsync(
        string[] args,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(mediator);
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            WriteHelp();
            return 0;
        }

        var options = ParseOptions(args[0], args[1..]);
        if (!options.IsSuccess)
        {
            Console.Error.WriteLine(options.Problem!.Message);
            return 64;
        }

        var location = await mediator.Send(
            new ResolveWorkspaceStateQuery(
                options.Value.Path,
                options.Value.ConfigurationPath,
                options.Value.StateRoot),
            cancellationToken);
        if (!location.IsSuccess)
        {
            WriteError(options.Value.Json, location.Problem!);
            return 2;
        }

        return args[0] switch
        {
            "check" => await CheckAsync(mediator, location.Value, options.Value.Json, cancellationToken),
            "backup" => await BackupAsync(mediator, location.Value, options.Value.Json, cancellationToken),
            "restore" => await RestoreAsync(mediator, location.Value, options.Value, cancellationToken),
            "vacuum" => await VacuumAsync(mediator, location.Value, options.Value, cancellationToken),
            "diagnostics" => await DiagnosticsAsync(mediator, location.Value, options.Value.Json, cancellationToken),
            _ => UnknownOperation(args[0]),
        };
    }

    private static async Task<int> CheckAsync(
        IMediator mediator,
        Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        bool json,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CheckWorkspaceDatabaseCommand(location), cancellationToken);
        if (!result.IsSuccess)
        {
            WriteError(json, result.Problem!);
            return 2;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                result.Value,
                WorkspaceDatabaseCliJsonContext.Default.DatabaseIntegrityReport));
        }
        else
        {
            Console.WriteLine($"Integrity: {result.Value.IntegrityCheckResult}");
            Console.WriteLine($"Schema version: {result.Value.SchemaVersion}");
            Console.WriteLine($"Active graph revision: {result.Value.ActiveGraphRevision?.ToString() ?? "none"}");
            Console.WriteLine($"Foreign-key violations: {result.Value.ForeignKeyViolations.Count}");
        }

        return result.Value.IsHealthy ? 0 : 1;
    }

    private static async Task<int> BackupAsync(
        IMediator mediator,
        Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        bool json,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new BackupWorkspaceDatabaseCommand(location), cancellationToken);
        return WriteResult(result, json, WorkspaceDatabaseCliJsonContext.Default.DatabaseBackup, backup =>
            $"Backup: {backup.BackupPath}{Environment.NewLine}Schema version: {backup.SchemaVersion}");
    }

    private static async Task<int> RestoreAsync(
        IMediator mediator,
        Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        WorkspaceDatabaseCliOptions options,
        CancellationToken cancellationToken)
    {
        if (options.BackupPath is null)
        {
            WriteError(options.Json, Problem.Validation("Database restore requires --backup <managed-backup-path>."));
            return 64;
        }

        var result = await mediator.Send(
            new RestoreWorkspaceDatabaseCommand(location, options.BackupPath),
            cancellationToken);
        return WriteResult(result, options.Json, WorkspaceDatabaseCliJsonContext.Default.DatabaseRestore, restore =>
            $"Restored from: {restore.BackupPath}{Environment.NewLine}Schema version: {restore.SchemaVersion}");
    }

    private static async Task<int> VacuumAsync(
        IMediator mediator,
        Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        WorkspaceDatabaseCliOptions options,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new VacuumWorkspaceDatabaseCommand(location, options.Force),
            cancellationToken);
        return WriteResult(result, options.Json, WorkspaceDatabaseCliJsonContext.Default.DatabaseVacuumResult, vacuum =>
            $"Vacuumed: {vacuum.WasVacuumed}{Environment.NewLine}{vacuum.Reason}");
    }

    private static async Task<int> DiagnosticsAsync(
        IMediator mediator,
        Workspaces.InitializeWorkspace.WorkspaceStateLocation location,
        bool json,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExportDatabaseDiagnosticsCommand(location), cancellationToken);
        return WriteResult(result, json, WorkspaceDatabaseCliJsonContext.Default.DatabaseDiagnosticsExport, export =>
            $"Diagnostics export: {export.ExportPath}");
    }

    private static int WriteResult<T>(
        Result<T> result,
        bool json,
        JsonTypeInfo<T> typeInfo,
        Func<T, string> plainText)
    {
        if (!result.IsSuccess)
        {
            WriteError(json, result.Problem!);
            return 2;
        }

        Console.WriteLine(json
            ? JsonSerializer.Serialize(result.Value, typeInfo)
            : plainText(result.Value));
        return 0;
    }

    private static Result<WorkspaceDatabaseCliOptions> ParseOptions(string operation, string[] args)
    {
        if (operation is not ("check" or "backup" or "restore" or "vacuum" or "diagnostics"))
        {
            return ResultFactory.Failure<WorkspaceDatabaseCliOptions>(
                Problem.Validation($"Unknown database command '{operation}'."));
        }

        var path = Directory.GetCurrentDirectory();
        string? stateRoot = null;
        string? configurationPath = null;
        string? backupPath = null;
        var force = false;
        var json = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--path" when index + 1 < args.Length:
                    path = args[++index];
                    break;
                case "--state-root" when index + 1 < args.Length:
                    stateRoot = args[++index];
                    break;
                case "--config" when index + 1 < args.Length:
                    configurationPath = args[++index];
                    break;
                case "--backup" when operation == "restore" && index + 1 < args.Length:
                    backupPath = args[++index];
                    break;
                case "--force" when operation == "vacuum":
                    force = true;
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ResultFactory.Failure<WorkspaceDatabaseCliOptions>(
                        Problem.Validation($"Unknown or incomplete database {operation} option '{args[index]}'."));
            }
        }

        return ResultFactory.Success(new WorkspaceDatabaseCliOptions(
            path,
            stateRoot,
            configurationPath,
            backupPath,
            force,
            json));
    }

    private static int UnknownOperation(string operation)
    {
        Console.Error.WriteLine($"Unknown database command '{operation}'. Run 'archy db --help' for available commands.");
        return 64;
    }

    private static void WriteError(bool json, Problem problem)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new WorkspaceDatabaseCliError(problem.Code, problem.Message),
                WorkspaceDatabaseCliJsonContext.Default.WorkspaceDatabaseCliError));
            return;
        }

        Console.Error.WriteLine($"{problem.Code}: {problem.Message}");
    }

    private static void WriteHelp()
    {
        Console.WriteLine("Usage: archy db <check|backup|restore|vacuum|diagnostics> [options]");
        Console.WriteLine("  --path <path> --state-root <path> --config <path> --json");
        Console.WriteLine("  restore requires --backup <managed-backup-path>");
        Console.WriteLine("  vacuum accepts --force");
    }

    private sealed record WorkspaceDatabaseCliOptions(
        string Path,
        string? StateRoot,
        string? ConfigurationPath,
        string? BackupPath,
        bool Force,
        bool Json);

    private sealed record WorkspaceDatabaseCliError(string Code, string Message);

    [JsonSerializable(typeof(DatabaseIntegrityReport))]
    [JsonSerializable(typeof(DatabaseBackup))]
    [JsonSerializable(typeof(DatabaseRestore))]
    [JsonSerializable(typeof(DatabaseVacuumResult))]
    [JsonSerializable(typeof(DatabaseDiagnosticsExport))]
    [JsonSerializable(typeof(WorkspaceDatabaseCliError))]
    private sealed partial class WorkspaceDatabaseCliJsonContext : JsonSerializerContext;
}
