using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Analysis.AnalyzeWorkspace;

public sealed record AnalyzeWorkspaceCommand(
    string StartPath,
    string? ExplicitConfigurationPath,
    string? StateRootOverride)
    : IRequest<Result<WorkspaceAnalysis>>;
