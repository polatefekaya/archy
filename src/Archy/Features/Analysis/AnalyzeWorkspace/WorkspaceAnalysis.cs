using Archy.Features.Analysis.ExtractCSharpSyntaxFacts;
using Archy.Features.Analysis.InventorySources;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Analysis.ResolveDotNetDependencyRegistrations;
using Archy.Features.Analysis.ResolveDotNetDependencyConsumptions;
using Archy.Features.Analysis.ResolveDotNetConfigurationReads;
using Archy.Features.Analysis.ResolveJsonConfigurationDefinitions;
using Archy.Features.Analysis.ResolveRabbitMqTopology;
using Archy.Features.Analysis.ResolveDotNetMessageContracts;
using Archy.Features.Analysis.PlanIncrementalAnalysis;
using Archy.Features.Graph.CommitGraphRevision;

namespace Archy.Features.Analysis.AnalyzeWorkspace;

public sealed record WorkspaceAnalysis(
    bool IsComplete,
    bool WasNoOp,
    string? RunId,
    SourceInventory Inventory,
    CSharpSyntaxFactBatch? CSharpSyntaxFacts,
    IReadOnlyList<SemanticAnalysisResult> LanguageServerSemanticFacts,
    DotNetDependencyRegistrationFacts? DependencyRegistrationFacts,
    DotNetDependencyConsumptionFacts? DependencyConsumptionFacts,
    DotNetConfigurationReadFacts? ConfigurationReadFacts,
    JsonConfigurationDefinitionFacts? ConfigurationDefinitionFacts,
    RabbitMqTopologyFacts? RabbitMqTopologyFacts,
    DotNetMessageContractFacts? MessageContractFacts,
    CommittedGraphRevision? GraphRevision,
    IncrementalAnalysisPlan? IncrementalPlan = null);
