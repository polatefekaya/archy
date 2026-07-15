using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Analysis.AnalyzeLanguageServerSemantics;
using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Analysis.MapSemanticGraphFacts;
using Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;
using Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;
using Archy.Features.Analysis.ResolveDotNetConfigurationReads;
using Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;
using Archy.Features.Analysis.ResolveRabbitMqTopology;
using Archy.Features.Analysis.ResolveDotNetMessageContracts;
using Archy.Features.Analysis.PlanIncrementalAnalysis;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadActiveGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.Features.Workspaces.ReadRepositoryCommit;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Analysis.AnalyzeWorkspace;

public sealed class AnalyzeWorkspaceHandler(
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader,
    IWorkspaceStateLayout stateLayout,
    IRepositorySourceInventory sourceInventory,
    IActiveGraphRevisionReader activeGraphRevisionReader,
    IGraphRevisionSnapshotReader graphRevisionSnapshotReader,
    IIncrementalAnalysisPlanner incrementalAnalysisPlanner,
    ICSharpSyntaxFactReconciler csharpSyntaxFactReconciler,
    IRepositoryProvenanceReader repositoryProvenanceReader,
    IAnalysisRunRepository analysisRuns,
    ICSharpSyntaxFactExtractor csharpSyntaxFactExtractor,
    IConfiguredLspSemanticAnalyzer configuredLspSemanticAnalyzer,
    IDotNetDependencyRegistrationProvider dependencyRegistrationProvider,
    IDotNetDependencyConsumptionProvider dependencyConsumptionProvider,
    IDotNetConfigurationReadProvider configurationReadProvider,
    IJsonConfigurationDefinitionProvider configurationDefinitionProvider,
    IRabbitMqTopologyProvider rabbitMqTopologyProvider,
    IDotNetMessageContractProvider messageContractProvider,
    IGraphRevisionCommitter graphRevisionCommitter)
    : IRequestHandler<AnalyzeWorkspaceCommand, Result<WorkspaceAnalysis>>, IWorkspaceAnalyzer
{
    private const string AnalyzerVersion = "syntax-configured-lsp-symbols-v3-incremental";

    public ValueTask<Result<WorkspaceAnalysis>> Handle(
        AnalyzeWorkspaceCommand command,
        CancellationToken cancellationToken) =>
        AnalyzeAsync(command, cancellationToken);

    public async ValueTask<Result<WorkspaceAnalysis>> AnalyzeAsync(
        AnalyzeWorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var workspace = workspaceLocator.Locate(command.StartPath);
        if (!workspace.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceAnalysis>(workspace.Problem!);
        }

        var configuration = await configurationLoader.LoadAsync(
            workspace.Value,
            command.ExplicitConfigurationPath,
            command.StateRootOverride,
            cancellationToken);
        if (!configuration.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceAnalysis>(configuration.Problem!);
        }

        var location = stateLayout.Resolve(workspace.Value, configuration.Value.StateRoot);
        if (!location.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceAnalysis>(location.Problem!);
        }

        var inventory = await sourceInventory.SynchronizeAsync(
            location.Value,
            workspace.Value.RepositoryRoot,
            configuration.Value.Configuration,
            cancellationToken);
        if (!inventory.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceAnalysis>(inventory.Problem!);
        }

        if (!inventory.Value.IsComplete)
        {
            return ResultFactory.Success(new WorkspaceAnalysis(
                IsComplete: false,
                WasNoOp: false,
                RunId: null,
                inventory.Value,
                CSharpSyntaxFacts: null,
                LanguageServerSemanticFacts: [],
                DependencyRegistrationFacts: null,
                DependencyConsumptionFacts: null,
                ConfigurationReadFacts: null,
                ConfigurationDefinitionFacts: null,
                RabbitMqTopologyFacts: null,
                MessageContractFacts: null,
                GraphRevision: null));
        }

        var activeRevision = await activeGraphRevisionReader.ReadAsync(location.Value, cancellationToken);
        if (!activeRevision.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceAnalysis>(activeRevision.Problem!);
        }

        var hasSourceChanges = inventory.Value.Changes.Any(static change =>
            change.Kind is SourceFileChangeKind.Added or SourceFileChangeKind.Changed or SourceFileChangeKind.Moved or SourceFileChangeKind.Deleted);
        if (!hasSourceChanges && activeRevision.Value is not null)
        {
            return ResultFactory.Success(new WorkspaceAnalysis(
                IsComplete: true,
                WasNoOp: true,
                RunId: null,
                inventory.Value,
                CSharpSyntaxFacts: null,
                LanguageServerSemanticFacts: [],
                DependencyRegistrationFacts: null,
                DependencyConsumptionFacts: null,
                ConfigurationReadFacts: null,
                ConfigurationDefinitionFacts: null,
                RabbitMqTopologyFacts: null,
                MessageContractFacts: null,
                GraphRevision: null));
        }

        GraphRevisionSnapshot? activeSnapshot = null;
        if (activeRevision.Value is not null)
        {
            var snapshot = await graphRevisionSnapshotReader.ReadActiveAsync(location.Value, cancellationToken);
            if (!snapshot.IsSuccess)
            {
                return ResultFactory.Failure<WorkspaceAnalysis>(snapshot.Problem!);
            }

            if (snapshot.Value is null)
            {
                return ResultFactory.Failure<WorkspaceAnalysis>(
                    Problem.Conflict("The workspace reports an active graph revision, but its immutable graph snapshot is unavailable."));
            }

            activeSnapshot = snapshot.Value;
        }

        var incrementalPlan = incrementalAnalysisPlanner.Create(inventory.Value, activeSnapshot);

        var repositoryProvenance = await repositoryProvenanceReader.ReadAsync(workspace.Value, cancellationToken);
        if (!repositoryProvenance.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceAnalysis>(repositoryProvenance.Problem!);
        }

        var run = await analysisRuns.StartAsync(
            location.Value,
            AnalyzerVersion,
            ConfigurationFingerprint.Calculate(configuration.Value.Configuration),
            repositoryProvenance.Value,
            cancellationToken);
        if (!run.IsSuccess)
        {
            return ResultFactory.Failure<WorkspaceAnalysis>(run.Problem!);
        }

        try
        {
            var parsedSyntaxFacts = await csharpSyntaxFactExtractor.ExtractAsync(
                workspace.Value.RepositoryRoot,
                incrementalPlan.CSharpFilesToParse,
                cancellationToken);
            if (!parsedSyntaxFacts.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(parsedSyntaxFacts.Problem!);
            }

            if (!parsedSyntaxFacts.Value.IsComplete)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Success(new WorkspaceAnalysis(
                    IsComplete: false,
                    WasNoOp: false,
                    run.Value.RunId,
                    inventory.Value,
                    parsedSyntaxFacts.Value,
                    LanguageServerSemanticFacts: [],
                    DependencyRegistrationFacts: null,
                    DependencyConsumptionFacts: null,
                    ConfigurationReadFacts: null,
                    ConfigurationDefinitionFacts: null,
                    RabbitMqTopologyFacts: null,
                    MessageContractFacts: null,
                    GraphRevision: null,
                    IncrementalPlan: incrementalPlan));
            }

            var syntaxFacts = incrementalPlan.ReusesCSharpSyntaxFacts
                ? csharpSyntaxFactReconciler.Reconcile(activeSnapshot!, incrementalPlan, parsedSyntaxFacts.Value)
                : parsedSyntaxFacts;
            if (!syntaxFacts.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(syntaxFacts.Problem!);
            }

            var configurationFingerprint = ConfigurationFingerprint.Calculate(configuration.Value.Configuration);
            var languageServerSemanticFacts = new List<SemanticAnalysisResult>();
            var semanticGraphFacts = new List<SemanticGraphFacts>();
            var contentHashes = inventory.Value.Files.ToDictionary(static file => file.RepositoryRelativePath, static file => file.ContentHash, StringComparer.Ordinal);
            foreach (var profile in configuration.Value.Configuration.LanguageServerProfiles.OrderBy(static profile => profile.Id, StringComparer.Ordinal))
            {
                var documents = inventory.Value.Files
                    .Where(file => profile.Extensions.Any(extension => file.RepositoryRelativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(static file => file.RepositoryRelativePath, StringComparer.Ordinal)
                    .Select(file => new SemanticDocument(file.RepositoryRelativePath, file.ContentHash, profile.LanguageId))
                    .ToArray();
                if (documents.Length == 0)
                {
                    continue;
                }

                var semanticFacts = await configuredLspSemanticAnalyzer.AnalyzeAsync(
                    new ConfiguredLspSemanticAnalysisRequest(
                        new SemanticAnalysisRequest(
                            SemanticAnalysisContract.CurrentSchemaVersion,
                            workspace.Value.RepositoryRoot,
                            run.Value.RunId,
                            documents),
                        profile,
                        configurationFingerprint),
                    cancellationToken);
                if (!semanticFacts.IsSuccess)
                {
                    await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                    return ResultFactory.Failure<WorkspaceAnalysis>(semanticFacts.Problem!);
                }

                var graphFacts = SemanticGraphFactMapper.Map(
                    $"{semanticFacts.Value.AdapterId}-semantic",
                    semanticFacts.Value.Symbols,
                    semanticFacts.Value.Definitions,
                    semanticFacts.Value.References,
                    semanticFacts.Value.Calls,
                    semanticFacts.Value.Inheritance,
                    contentHashes);
                if (!graphFacts.IsSuccess)
                {
                    await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                    return ResultFactory.Failure<WorkspaceAnalysis>(graphFacts.Problem!);
                }

                languageServerSemanticFacts.Add(semanticFacts.Value);
                semanticGraphFacts.Add(graphFacts.Value);
            }

            var dependencyRegistrations = await dependencyRegistrationProvider.ResolveAsync(
                workspace.Value.RepositoryRoot,
                inventory.Value.Files,
                syntaxFacts.Value.Nodes,
                cancellationToken);
            if (!dependencyRegistrations.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(dependencyRegistrations.Problem!);
            }

            var dependencyConsumptions = await dependencyConsumptionProvider.ResolveAsync(
                workspace.Value.RepositoryRoot,
                inventory.Value.Files,
                syntaxFacts.Value.Nodes,
                dependencyRegistrations.Value.Edges,
                cancellationToken);
            if (!dependencyConsumptions.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(dependencyConsumptions.Problem!);
            }

            var configurationReads = await configurationReadProvider.ResolveAsync(
                workspace.Value.RepositoryRoot,
                inventory.Value.Files,
                syntaxFacts.Value.Nodes,
                cancellationToken);
            if (!configurationReads.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(configurationReads.Problem!);
            }

            var configurationDefinitions = await configurationDefinitionProvider.ResolveAsync(
                workspace.Value.RepositoryRoot,
                inventory.Value.Files,
                configurationReads.Value.Nodes,
                cancellationToken);
            if (!configurationDefinitions.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(configurationDefinitions.Problem!);
            }

            var rabbitMqTopology = await rabbitMqTopologyProvider.ResolveAsync(
                workspace.Value.RepositoryRoot,
                inventory.Value.Files,
                syntaxFacts.Value.Nodes,
                cancellationToken);
            if (!rabbitMqTopology.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(rabbitMqTopology.Problem!);
            }

            var messageContracts = await messageContractProvider.ResolveAsync(
                workspace.Value.RepositoryRoot,
                inventory.Value.Files,
                syntaxFacts.Value.Nodes,
                cancellationToken);
            if (!messageContracts.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(messageContracts.Problem!);
            }

            var committed = await graphRevisionCommitter.CommitAsync(
                location.Value,
                run.Value.RunId,
                [.. syntaxFacts.Value.Nodes
                    .Concat(semanticGraphFacts.SelectMany(static facts => facts.Nodes))
                    .Concat(configurationReads.Value.Nodes)
                    .Concat(configurationDefinitions.Value.Nodes)
                    .Concat(rabbitMqTopology.Value.Nodes)],
                [.. syntaxFacts.Value.Edges
                    .Concat(semanticGraphFacts.SelectMany(static facts => facts.Edges))
                    .Concat(dependencyRegistrations.Value.Edges)
                    .Concat(dependencyConsumptions.Value.Edges)
                    .Concat(configurationReads.Value.Edges)
                    .Concat(configurationDefinitions.Value.Edges)
                    .Concat(rabbitMqTopology.Value.Edges)
                    .Concat(messageContracts.Value.Edges)],
                [.. syntaxFacts.Value.Symbols.Concat(semanticGraphFacts.SelectMany(static facts => facts.Symbols))],
                syntaxFacts.Value.InterfaceFingerprints,
                cancellationToken);
            if (!committed.IsSuccess)
            {
                await CompleteFailedRunAsync(location.Value, run.Value.RunId);
                return ResultFactory.Failure<WorkspaceAnalysis>(committed.Problem!);
            }

            return ResultFactory.Success(new WorkspaceAnalysis(
                IsComplete: true,
                WasNoOp: false,
                run.Value.RunId,
                inventory.Value,
                syntaxFacts.Value,
                [.. languageServerSemanticFacts],
                dependencyRegistrations.Value,
                dependencyConsumptions.Value,
                configurationReads.Value,
                configurationDefinitions.Value,
                rabbitMqTopology.Value,
                messageContracts.Value,
                committed.Value,
                incrementalPlan));
        }
        catch (OperationCanceledException)
        {
            await CompleteCancelledRunAsync(location.Value, run.Value.RunId);
            throw;
        }
    }

    private async Task CompleteFailedRunAsync(WorkspaceStateLocation location, string runId)
    {
        _ = await analysisRuns.CompleteAsync(
            location,
            runId,
            AnalysisRunStatus.Failed,
            graphRevision: null,
            CancellationToken.None);
    }

    private async Task CompleteCancelledRunAsync(WorkspaceStateLocation location, string runId)
    {
        _ = await analysisRuns.CompleteAsync(
            location,
            runId,
            AnalysisRunStatus.Cancelled,
            graphRevision: null,
            CancellationToken.None);
    }
}
