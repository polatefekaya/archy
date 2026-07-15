namespace Archy.Features.Storage.WorkspaceDatabase.Vacuum;

public sealed record DatabaseVacuumResult(
    bool WasVacuumed,
    long PageCountBefore,
    long FreelistPageCountBefore,
    string Reason);
