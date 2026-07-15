namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed record ArchyConfiguration(
    int SchemaVersion,
    WorkspaceConfiguration Workspace,
    LocalStateConfiguration LocalState,
    LanguageServerProfileConfiguration[] LanguageServerProfiles,
    ProviderConfiguration Providers,
    ProviderPatternConfiguration[] ProviderPatterns,
    LayerRuleConfiguration[] Layers,
    ArchitectureEnforcementConfiguration Enforcement,
    ModelConfiguration Model,
    MemorySelectionConfiguration Memory,
    SidecarConfiguration Sidecars,
    ScopeConfiguration Scope,
    HealthWeightConfiguration HealthWeights)
{
    public static ArchyConfiguration Default { get; } = new(
        SchemaVersion: 1,
        Workspace: new WorkspaceConfiguration(null),
        LocalState: new LocalStateConfiguration(null),
        LanguageServerProfiles:
        [
            new LanguageServerProfileConfiguration(
                "csharp",
                "csharp",
                [".cs"],
                ["*.sln", "*.slnx", "*.csproj"],
                "Microsoft.CodeAnalysis.LanguageServer",
                ["--stdio"],
                "csharp",
                [
                    new LanguageServerSymbolKindMapping("namespace", [3]),
                    new LanguageServerSymbolKindMapping("type", [5, 10, 11, 23]),
                    new LanguageServerSymbolKindMapping("method", [6, 9, 12]),
                    new LanguageServerSymbolKindMapping("property", [7]),
                    new LanguageServerSymbolKindMapping("field", [8]),
                    new LanguageServerSymbolKindMapping("event", [24]),
                    new LanguageServerSymbolKindMapping("parameter", [26]),
                ],
                MaxSymbolQueries: 10_000)
        ],
        Providers: new ProviderConfiguration(
            DependencyInjection: true,
            Messaging: true,
            EntityFrameworkCore: true,
            Cache: true,
            Configuration: true),
        ProviderPatterns: [],
        Layers: [],
        Enforcement: new ArchitectureEnforcementConfiguration(["calls", "references", "inherits"]),
        Model: new ModelConfiguration(
            Provider: "openai",
            SummaryModel: null,
            EmbeddingModel: null,
            MaxRequestsPerRun: 30,
            MaxTokensPerRun: 200_000,
            MaxCostUsdPerRun: 20,
            MaxConcurrentRequests: 2,
            RateLimitCooldownSeconds: 30),
        Memory: new MemorySelectionConfiguration(
            IncludeGeneratedNodes: false,
            ImportantModulePaths: [],
            SourceSharing: AiSourceSharingMode.Disabled),
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

public sealed record LanguageServerProfileConfiguration(
    string Id,
    string LanguageId,
    string[] Extensions,
    string[] Markers,
    string Command,
    string[] Arguments,
    string SymbolIdentityPrefix,
    LanguageServerSymbolKindMapping[] SymbolKinds,
    int MaxSymbolQueries = 10_000);

public sealed record LanguageServerSymbolKindMapping(string SemanticKind, int[] LspKinds);

public sealed record ProviderConfiguration(
    bool DependencyInjection,
    bool Messaging,
    bool EntityFrameworkCore,
    bool Cache,
    bool Configuration);

public sealed record ProviderPatternConfiguration(
    string Id,
    string Framework,
    string MatchKind,
    string Member,
    string? Type,
    string? CaptureName,
    int? CaptureArgumentIndex);

public sealed record LayerRuleConfiguration(
    string Name,
    string[] Includes,
    string[] MayDependOn);

public sealed record ArchitectureEnforcementConfiguration(string[] HardEdgeKinds);

public sealed record ModelConfiguration(
    string Provider,
    string? SummaryModel,
    string? EmbeddingModel,
    int MaxRequestsPerRun,
    int MaxTokensPerRun,
    double MaxCostUsdPerRun,
    int MaxConcurrentRequests,
    int RateLimitCooldownSeconds);

/// <summary>Repository-controlled selection policy for deterministic memory targets.</summary>
public sealed record MemorySelectionConfiguration(
    bool IncludeGeneratedNodes,
    string[] ImportantModulePaths,
    AiSourceSharingMode SourceSharing);

/// <summary>Repository-owned consent boundary for any source-derived request leaving the machine.</summary>
public enum AiSourceSharingMode
{
    Disabled,
    SummariesOnly,
    SummariesAndEmbeddings,
}

public sealed record SidecarConfiguration(string? JscpdCommand, string? LouvainCommand);

public sealed record ScopeConfiguration(string[] Include, string[] Exclude);

public sealed record HealthWeightConfiguration(
    double Architecture,
    double Duplicates,
    double Documentation,
    double Decisions);
