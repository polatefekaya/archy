using Archy.Features.Analysis.AnalysisRuns;
using Archy.Features.Analysis.AnalyzeLanguageServerSemantics;
using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;
using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;
using Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;
using Archy.Features.Analysis.ResolveDotNetConfigurationReads;
using Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;
using Archy.Features.Analysis.ResolveRabbitMqTopology;
using Archy.Features.Analysis.ResolveDotNetMessageContracts;
using Archy.Features.Analysis.LanguageServerProfiles;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadActiveGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Analysis.PlanIncrementalAnalysis;
using Archy.Features.Integrations.Codex.PublishAgentsSnapshot;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.Features.Workspaces.ReadRepositoryCommit;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Analysis.AnalyzeWorkspace;

internal static class AnalyzeWorkspaceTestSupport
{
    public static AnalyzeWorkspaceHandler CreateHandler(IConfiguredLspSemanticAnalyzer? semanticAnalyzer = null)
    {
        var lockManager = new WorkspaceLockManager(TimeProvider.System);
        return new AnalyzeWorkspaceHandler(
            new WorkspaceLocator(),
            TestConfigurationFactory.CreateLoader(),
            new WorkspaceStateLayout(),
            new RepositorySourceInventory(TimeProvider.System, lockManager),
            new ActiveGraphRevisionReader(lockManager),
            new GraphRevisionSnapshotReader(lockManager),
            new IncrementalAnalysisPlanner(),
            new CSharpSyntaxFactReconciler(),
            new GitRepositoryCommitReader(),
            new AnalysisRunRepository(TimeProvider.System, lockManager),
            new CSharpSyntaxFactExtractor(),
            semanticAnalyzer ?? new ConfiguredLspSemanticAnalyzer(
                new LanguageServerProfileSelector(new PathExecutablePathProbe()),
                new LanguageServerStdioSessionFactory()),
            new DotNetDependencyRegistrationProvider(),
            new DotNetDependencyConsumptionProvider(),
            new DotNetConfigurationReadProvider(),
            new JsonConfigurationDefinitionProvider(),
            new RabbitMqTopologyProvider(),
            new DotNetMessageContractProvider(),
            new GraphRevisionCommitter(TimeProvider.System, lockManager),
            new NoOpAgentsPublisher());
    }

    private sealed class NoOpAgentsPublisher : IPostRevisionAgentsPublisher
    {
        public ValueTask<Archy.SharedKernel.Primitives.Result<bool>> PublishAsync(
            string repositoryRoot,
            Archy.Features.Configuration.LoadEffectiveConfiguration.ArchyConfiguration configuration,
            long graphRevision,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(Archy.SharedKernel.Primitives.ResultFactory.Success(false));
    }
}
