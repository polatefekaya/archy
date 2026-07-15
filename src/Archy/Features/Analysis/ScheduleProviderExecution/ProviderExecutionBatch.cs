using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.ProviderSiteMatches;

namespace Archy.Features.Analysis.ScheduleProviderExecution;

public sealed record ProviderExecutionSnapshot(
    string RepositoryRoot,
    IReadOnlyList<SourceFile> Files,
    string SnapshotId);

public sealed record ProviderExecutionBatch(
    string SnapshotId,
    IReadOnlyList<ProviderExecutionResult> Providers);

public sealed record ProviderExecutionResult(
    string ProviderId,
    ProviderExecutionState State,
    IReadOnlyList<ProviderSiteMatch> Matches,
    ProviderExecutionDiagnostic? Diagnostic);

public sealed record ProviderExecutionDiagnostic(string Code, string Message);

public enum ProviderExecutionState
{
    Succeeded,
    Degraded,
}
