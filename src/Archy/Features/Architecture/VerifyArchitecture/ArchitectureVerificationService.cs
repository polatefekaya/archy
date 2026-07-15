using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Architecture.VerifyArchitecture;

/// <summary>Refreshes the graph, evaluates hard rules, and compares the stable results with repository policy.</summary>
public sealed class ArchitectureVerificationService(
    IWorkspaceAnalyzer workspaceAnalyzer,
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader,
    IWorkspaceStateLayout stateLayout,
    IGraphRevisionSnapshotReader graphRevisionSnapshotReader,
    ILayerDependencyRuleEvaluator ruleEvaluator,
    IArchitectureFindingFactory findingFactory,
    IArchitectureBaselinePolicyRepository baselineRepository,
    IArchitectureBaselineComparer baselineComparer,
    IArchitectureExceptionPolicyRepository exceptionRepository,
    IArchitectureExceptionApplicator exceptionApplicator,
    TimeProvider timeProvider)
    : IArchitectureVerificationService
{
    public async ValueTask<Result<ArchitectureVerification>> VerifyAsync(
        VerifyArchitectureCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var analysis = await workspaceAnalyzer.AnalyzeAsync(
            new AnalyzeWorkspaceCommand(command.StartPath, command.ExplicitConfigurationPath, command.StateRootOverride),
            cancellationToken);
        if (!analysis.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(analysis.Problem!);
        }

        if (!analysis.Value.IsComplete)
        {
            return ResultFactory.Failure<ArchitectureVerification>(
                Problem.Conflict("Architecture verification requires a complete workspace analysis."));
        }

        var workspace = workspaceLocator.Locate(command.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(workspace.Problem!);
        }

        var configuration = await configurationLoader.LoadAsync(
            workspace.Value,
            command.ExplicitConfigurationPath,
            command.StateRootOverride,
            cancellationToken);
        if (!configuration.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(configuration.Problem!);
        }

        if (configuration.Value.Configuration.Layers.Length == 0)
        {
            return ResultFactory.Failure<ArchitectureVerification>(
                Problem.Validation("Architecture verification requires at least one configured layer rule."));
        }

        var location = stateLayout.Resolve(workspace.Value, configuration.Value.StateRoot);
        if (!location.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(location.Problem!);
        }

        var graph = await graphRevisionSnapshotReader.ReadActiveAsync(location.Value, cancellationToken);
        if (!graph.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(graph.Problem!);
        }

        if (graph.Value is null)
        {
            return ResultFactory.Failure<ArchitectureVerification>(
                Problem.Conflict("Architecture verification requires an active graph revision."));
        }

        var evaluation = ruleEvaluator.Evaluate(
            graph.Value.Nodes,
            graph.Value.Edges,
            configuration.Value.Configuration.Layers,
            configuration.Value.Configuration.Enforcement);
        if (!evaluation.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(evaluation.Problem!);
        }

        var findings = findingFactory.Create(evaluation.Value);
        if (!findings.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(findings.Problem!);
        }

        var baseline = await baselineRepository.ReadAsync(workspace.Value.RepositoryRoot, cancellationToken);
        if (!baseline.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(baseline.Problem!);
        }

        var ruleFingerprint = ArchitectureRuleFingerprint.Calculate(
            configuration.Value.Configuration.Layers,
            configuration.Value.Configuration.Enforcement);
        var comparison = baselineComparer.Compare(
            ruleFingerprint,
            baseline.Value.Path,
            findings.Value,
            baseline.Value.Baseline);
        if (!comparison.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(comparison.Problem!);
        }

        var exceptions = await exceptionRepository.ReadAsync(workspace.Value.RepositoryRoot, cancellationToken);
        if (!exceptions.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(exceptions.Problem!);
        }

        var exceptionApplication = exceptionApplicator.Apply(
            comparison.Value,
            exceptions.Value.Policy,
            timeProvider.GetUtcNow());
        if (!exceptionApplication.IsSuccess)
        {
            return ResultFactory.Failure<ArchitectureVerification>(exceptionApplication.Problem!);
        }

        return ResultFactory.Success(new ArchitectureVerification(
            graph.Value.Revision,
            analysis.Value.WasNoOp,
            ruleFingerprint,
            evaluation.Value,
            exceptionApplication.Value.Comparison,
            exceptionApplication.Value.Exceptions,
            CreateSourceLocations(graph.Value.Nodes, exceptionApplication.Value.Comparison)));
    }

    private static IReadOnlyList<ArchitectureVerificationSourceLocation> CreateSourceLocations(
        IReadOnlyList<GraphNodeFact> nodes,
        ArchitectureBaselineComparison comparison)
    {
        var findingNodeIds = comparison.Findings
            .Where(static outcome => outcome.Status != ArchitectureFindingStatus.Resolved)
            .SelectMany(static outcome => outcome.Finding.Targets)
            .Where(static target => target.Kind == ArchitectureTargetKind.GraphNode)
            .Select(static target => target.StableId)
            .ToHashSet(StringComparer.Ordinal);

        return [.. nodes
            .Where(node => findingNodeIds.Contains(node.StableId) && !string.IsNullOrWhiteSpace(node.FilePath))
            .OrderBy(static node => node.StableId, StringComparer.Ordinal)
            .Select(static node => new ArchitectureVerificationSourceLocation(
                node.StableId,
                node.FilePath!,
                node.StartLine,
                node.EndLine))];
    }
}
