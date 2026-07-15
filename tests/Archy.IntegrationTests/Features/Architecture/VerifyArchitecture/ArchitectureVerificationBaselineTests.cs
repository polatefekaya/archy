using Archy.Features.Analysis.AnalyzeWorkspace;
using Archy.Features.Architecture.ArchitectureExceptions;
using Archy.Features.Architecture.DetectDependencyCycles;
using Archy.Features.Architecture.EnforceLayerDependencies;
using Archy.Features.Architecture.LayerMembership;
using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Architecture.VerifyArchitecture;
using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Workspaces.AcquireWorkspaceLock;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;
using Archy.SharedKernel.Primitives;

namespace Archy.IntegrationTests.Features.Architecture.VerifyArchitecture;

public sealed class ArchitectureVerificationBaselineTests
{
    [Fact]
    public async Task FailsOnlyNewDeterministicDebtAfterAnExplicitBaselineAcceptance()
    {
        using var fixture = WorkspaceStateFixture.Create();
        var initialized = await fixture.InitializeAsync();
        Assert.True(initialized.IsSuccess);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Repository.Root, "archy.toml"),
            """
            schema_version = 1

            [[layers]]
            name = "Presentation"
            include = ["src/Presentation/**"]
            may_depend_on = ["Application"]

            [[layers]]
            name = "Application"
            include = ["src/Application/**"]
            may_depend_on = ["Domain"]

            [[layers]]
            name = "Domain"
            include = ["src/Domain/**"]
            may_depend_on = []
            """);
        var domain = Node("node:domain", "src/Domain/Order.cs");
        var application = Node("node:application", "src/Application/OrderHandler.cs");
        var presentation = Node("node:presentation", "src/Presentation/OrdersEndpoint.cs");
        await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [domain, application],
            [Edge("edge:domain-application", domain.StableId, application.StableId)]);
        var baselineRepository = new ArchitectureBaselinePolicyRepository();
        var service = CreateService(baselineRepository);
        var command = new VerifyArchitectureCommand(fixture.Repository.Root, null, fixture.StateRoot);

        var beforeAcceptance = await service.VerifyAsync(command, CancellationToken.None);

        Assert.True(beforeAcceptance.IsSuccess, beforeAcceptance.IsSuccess ? string.Empty : beforeAcceptance.Problem!.Message);
        Assert.False(beforeAcceptance.Value.IsCompliant);
        Assert.Equal(ArchitectureBaselineStatus.Missing, beforeAcceptance.Value.Baseline.Status);
        var originalFinding = Assert.Single(beforeAcceptance.Value.Baseline.Findings);
        Assert.Equal(ArchitectureFindingStatus.Introduced, originalFinding.Status);

        var baseline = new ArchitectureVerificationBaseline(
            1,
            beforeAcceptance.Value.RuleFingerprint,
            beforeAcceptance.Value.GraphRevision,
            DateTimeOffset.Parse("2026-07-15T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            [.. beforeAcceptance.Value.Baseline.CurrentFindings]);
        var written = await baselineRepository.WriteAsync(fixture.Repository.Root, baseline, CancellationToken.None);
        Assert.True(written.IsSuccess, written.IsSuccess ? string.Empty : written.Problem!.Message);

        var legacyOnly = await service.VerifyAsync(command, CancellationToken.None);

        Assert.True(legacyOnly.IsSuccess, legacyOnly.IsSuccess ? string.Empty : legacyOnly.Problem!.Message);
        Assert.True(legacyOnly.Value.IsCompliant);
        var legacy = Assert.Single(legacyOnly.Value.Baseline.Findings);
        Assert.Equal(ArchitectureFindingStatus.Legacy, legacy.Status);
        Assert.Equal(originalFinding.Finding.Key, legacy.Finding.Key);

        await GraphRevisionTestBuilder.CommitAsync(
            initialized.Value.StateLocation,
            [domain, application, presentation],
            [
                Edge("edge:domain-application", domain.StableId, application.StableId),
                Edge("edge:application-presentation", application.StableId, presentation.StableId),
            ]);

        var withNewDebt = await service.VerifyAsync(command, CancellationToken.None);

        Assert.True(withNewDebt.IsSuccess, withNewDebt.IsSuccess ? string.Empty : withNewDebt.Problem!.Message);
        Assert.False(withNewDebt.Value.IsCompliant);
        Assert.Collection(
            withNewDebt.Value.Baseline.Findings,
            outcome => Assert.Equal(ArchitectureFindingStatus.Introduced, outcome.Status),
            outcome => Assert.Equal(ArchitectureFindingStatus.Legacy, outcome.Status));
    }

    private static ArchitectureVerificationService CreateService(
        IArchitectureBaselinePolicyRepository baselineRepository)
    {
        var lockManager = new WorkspaceLockManager(TimeProvider.System);
        return new ArchitectureVerificationService(
            new CompleteNoOpWorkspaceAnalyzer(),
            new WorkspaceLocator(),
            TestConfigurationFactory.CreateLoader(),
            new WorkspaceStateLayout(),
            new GraphRevisionSnapshotReader(lockManager),
            new LayerDependencyRuleEvaluator(
                new LayerMembershipResolver(),
                new HardArchitectureEdgePolicy(),
                new ArchitectureCycleDetector()),
            new ArchitectureFindingFactory(),
            baselineRepository,
            new ArchitectureBaselineComparer(),
            new ArchitectureExceptionPolicyRepository(),
            new ArchitectureExceptionApplicator(),
            TimeProvider.System);
    }

    private static GraphNodeFact Node(string stableId, string path) => new(
        stableId,
        "semantic_method",
        stableId,
        stableId,
        path,
        1,
        10,
        "fixture",
        1,
        "{}",
        $"hash:{stableId}");

    private static GraphEdgeFact Edge(string edgeId, string source, string target) => new(
        edgeId,
        source,
        target,
        "calls",
        null,
        "fixture",
        1,
        "{}");

    private sealed class CompleteNoOpWorkspaceAnalyzer : IWorkspaceAnalyzer
    {
        public ValueTask<Result<WorkspaceAnalysis>> AnalyzeAsync(
            AnalyzeWorkspaceCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ResultFactory.Success(new WorkspaceAnalysis(
                IsComplete: true,
                WasNoOp: true,
                RunId: null,
                Inventory: null!,
                CSharpSyntaxFacts: null,
                LanguageServerSemanticFacts: [],
                DependencyRegistrationFacts: null,
                DependencyConsumptionFacts: null,
                ConfigurationReadFacts: null,
                ConfigurationDefinitionFacts: null,
                RabbitMqTopologyFacts: null,
                MessageContractFacts: null,
                GraphRevision: null)));
        }
    }
}
