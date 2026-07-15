using Archy.SharedKernel.Primitives;

namespace Archy.Features.Analysis.AnalyzeWorkspace;

/// <summary>Runs one complete workspace analysis for features that require a current graph.</summary>
public interface IWorkspaceAnalyzer
{
    ValueTask<Result<WorkspaceAnalysis>> AnalyzeAsync(
        AnalyzeWorkspaceCommand command,
        CancellationToken cancellationToken);
}
