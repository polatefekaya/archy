namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed record ArchyConfiguration(
    int SchemaVersion,
    WorkspaceConfiguration Workspace,
    LocalStateConfiguration LocalState,
    CSharpLanguageServerConfiguration CSharpLanguageServer,
    ProviderConfiguration Providers,
    LayerRuleConfiguration[] Layers,
    ModelConfiguration Model,
    SidecarConfiguration Sidecars,
    ScopeConfiguration Scope,
    HealthWeightConfiguration HealthWeights)
{
    public static ArchyConfiguration Default { get; } = new(
        SchemaVersion: 1,
        Workspace: new WorkspaceConfiguration(null),
        LocalState: new LocalStateConfiguration(null),
        CSharpLanguageServer: new CSharpLanguageServerConfiguration(null, []),
        Providers: new ProviderConfiguration(
            DependencyInjection: true,
            Messaging: true,
            EntityFrameworkCore: true,
            Cache: true,
            Configuration: true),
        Layers: [],
        Model: new ModelConfiguration(
            Provider: "openai",
            SummaryModel: null,
            EmbeddingModel: null,
            MaxRequestsPerRun: 30,
            MaxTokensPerRun: 200_000),
        Sidecars: new SidecarConfiguration(null, null),
        Scope: new ScopeConfiguration([], ["**/bin/**", "**/obj/**", "**/.git/**"]),
        HealthWeights: new HealthWeightConfiguration(
            Architecture: 0.40,
            Duplicates: 0.20,
            Documentation: 0.20,
            Decisions: 0.20));
}

public sealed record WorkspaceConfiguration(string? DisplayName);

public sealed record LocalStateConfiguration(string? RootPath);

public sealed record CSharpLanguageServerConfiguration(string? Command, string[] Arguments);

public sealed record ProviderConfiguration(
    bool DependencyInjection,
    bool Messaging,
    bool EntityFrameworkCore,
    bool Cache,
    bool Configuration);

public sealed record LayerRuleConfiguration(
    string Name,
    string[] Includes,
    string[] MayDependOn);

public sealed record ModelConfiguration(
    string Provider,
    string? SummaryModel,
    string? EmbeddingModel,
    int MaxRequestsPerRun,
    int MaxTokensPerRun);

public sealed record SidecarConfiguration(string? JscpdCommand, string? LouvainCommand);

public sealed record ScopeConfiguration(string[] Include, string[] Exclude);

public sealed record HealthWeightConfiguration(
    double Architecture,
    double Duplicates,
    double Documentation,
    double Decisions);
