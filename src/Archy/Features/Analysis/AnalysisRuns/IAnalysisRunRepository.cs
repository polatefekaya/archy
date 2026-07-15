using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.ReadRepositoryCommit;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.AnalysisRuns;

public interface IAnalysisRunRepository
{
    ValueTask<Result<AnalysisRun>> StartAsync(WorkspaceStateLocation location, string analyzerVersion, string configurationHash, string? repositoryCommit, CancellationToken cancellationToken);

    ValueTask<Result<AnalysisRun>> StartAsync(WorkspaceStateLocation location, string analyzerVersion, string configurationHash, RepositoryProvenance repositoryProvenance, CancellationToken cancellationToken);

    ValueTask<Result<AnalysisRun>> CompleteAsync(WorkspaceStateLocation location, string runId, AnalysisRunStatus terminalStatus, long? graphRevision, CancellationToken cancellationToken);
}
