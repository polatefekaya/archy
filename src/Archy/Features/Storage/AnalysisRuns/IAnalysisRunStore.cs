using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Storage.AnalysisRuns;

public interface IAnalysisRunStore
{
    ValueTask<Result<AnalysisRun>> StartAsync(WorkspaceStateLocation location, string analyzerVersion, string configurationHash, string? repositoryCommit, CancellationToken cancellationToken);

    ValueTask<Result<AnalysisRun>> CompleteAsync(WorkspaceStateLocation location, string runId, AnalysisRunStatus terminalStatus, long? graphRevision, CancellationToken cancellationToken);
}
