namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed record ArchyConfigurationLayer(
    string? WorkspaceDisplayName,
    string? LocalStateRootPath,
    LanguageServerProfileConfiguration[]? LanguageServerProfiles,
    bool? DependencyInjectionProviderEnabled,
    bool? MessagingProviderEnabled,
    bool? EntityFrameworkCoreProviderEnabled,
    bool? CacheProviderEnabled,
    bool? ConfigurationProviderEnabled,
    ProviderPatternConfiguration[]? ProviderPatterns,
    LayerRuleConfiguration[]? Layers,
    string[]? EnforcementHardEdgeKinds,
    string? ModelProvider,
    string? SummaryModel,
    string? EmbeddingModel,
    int? MaxRequestsPerRun,
    int? MaxTokensPerRun,
    double? MaxCostUsdPerRun,
    int? MaxConcurrentModelRequests,
    int? ModelRateLimitCooldownSeconds,
    bool? IncludeGeneratedMemoryNodes,
    string[]? ImportantMemoryModulePaths,
    AiSourceSharingMode? AiSourceSharingMode,
    string? JscpdCommand,
    string? LouvainCommand,
    string[]? ScopeInclude,
    string[]? ScopeExclude,
    double? ArchitectureWeight,
    double? DuplicatesWeight,
    double? DocumentationWeight,
    double? DecisionsWeight,
    string? SimilarityPolicyVersion,
    double? SimilarityEmbeddingWeight,
    double? SimilaritySymbolWeight,
    double? SimilaritySignatureWeight,
    double? SimilarityDependencyNeighborhoodWeight,
    double? SimilarityFileContextWeight,
    double? SimilarityModuleContextWeight)
{
    public ArchyConfiguration ApplyTo(ArchyConfiguration current, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return current with
        {
            Workspace = current.Workspace with
            {
                DisplayName = WorkspaceDisplayName ?? current.Workspace.DisplayName,
            },
            LocalState = current.LocalState with
            {
                RootPath = LocalStateRootPath is null
                    ? current.LocalState.RootPath
                    : ResolvePath(LocalStateRootPath, sourcePath),
            },
            LanguageServerProfiles = LanguageServerProfiles is null
                ? current.LanguageServerProfiles
                : MergeLanguageServerProfiles(current.LanguageServerProfiles, LanguageServerProfiles),
            Providers = current.Providers with
            {
                DependencyInjection = DependencyInjectionProviderEnabled ?? current.Providers.DependencyInjection,
                Messaging = MessagingProviderEnabled ?? current.Providers.Messaging,
                EntityFrameworkCore = EntityFrameworkCoreProviderEnabled ?? current.Providers.EntityFrameworkCore,
                Cache = CacheProviderEnabled ?? current.Providers.Cache,
                Configuration = ConfigurationProviderEnabled ?? current.Providers.Configuration,
            },
            ProviderPatterns = ProviderPatterns ?? current.ProviderPatterns,
            Layers = Layers ?? current.Layers,
            Enforcement = current.Enforcement with
            {
                HardEdgeKinds = EnforcementHardEdgeKinds ?? current.Enforcement.HardEdgeKinds,
            },
            Model = current.Model with
            {
                Provider = ModelProvider ?? current.Model.Provider,
                SummaryModel = SummaryModel ?? current.Model.SummaryModel,
                EmbeddingModel = EmbeddingModel ?? current.Model.EmbeddingModel,
                MaxRequestsPerRun = MaxRequestsPerRun ?? current.Model.MaxRequestsPerRun,
                MaxTokensPerRun = MaxTokensPerRun ?? current.Model.MaxTokensPerRun,
                MaxCostUsdPerRun = MaxCostUsdPerRun ?? current.Model.MaxCostUsdPerRun,
                MaxConcurrentRequests = MaxConcurrentModelRequests ?? current.Model.MaxConcurrentRequests,
                RateLimitCooldownSeconds = ModelRateLimitCooldownSeconds ?? current.Model.RateLimitCooldownSeconds,
            },
            Memory = current.Memory with
            {
                IncludeGeneratedNodes = IncludeGeneratedMemoryNodes ?? current.Memory.IncludeGeneratedNodes,
                ImportantModulePaths = ImportantMemoryModulePaths ?? current.Memory.ImportantModulePaths,
                SourceSharing = AiSourceSharingMode ?? current.Memory.SourceSharing,
            },
            Sidecars = current.Sidecars with
            {
                JscpdCommand = JscpdCommand ?? current.Sidecars.JscpdCommand,
                LouvainCommand = LouvainCommand ?? current.Sidecars.LouvainCommand,
            },
            Scope = current.Scope with
            {
                Include = ScopeInclude ?? current.Scope.Include,
                Exclude = ScopeExclude ?? current.Scope.Exclude,
            },
            HealthWeights = current.HealthWeights with
            {
                Architecture = ArchitectureWeight ?? current.HealthWeights.Architecture,
                Duplicates = DuplicatesWeight ?? current.HealthWeights.Duplicates,
                Documentation = DocumentationWeight ?? current.HealthWeights.Documentation,
                Decisions = DecisionsWeight ?? current.HealthWeights.Decisions,
            },
            Similarity = current.Similarity with
            {
                PolicyVersion = SimilarityPolicyVersion ?? current.Similarity.PolicyVersion,
                EmbeddingWeight = SimilarityEmbeddingWeight ?? current.Similarity.EmbeddingWeight,
                SymbolWeight = SimilaritySymbolWeight ?? current.Similarity.SymbolWeight,
                SignatureWeight = SimilaritySignatureWeight ?? current.Similarity.SignatureWeight,
                DependencyNeighborhoodWeight = SimilarityDependencyNeighborhoodWeight ?? current.Similarity.DependencyNeighborhoodWeight,
                FileContextWeight = SimilarityFileContextWeight ?? current.Similarity.FileContextWeight,
                ModuleContextWeight = SimilarityModuleContextWeight ?? current.Similarity.ModuleContextWeight,
            },
        };
    }

    private static string ResolvePath(string configuredPath, string sourcePath)
    {
        return Path.IsPathFullyQualified(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, configuredPath));
    }

    private static LanguageServerProfileConfiguration[] MergeLanguageServerProfiles(
        IReadOnlyList<LanguageServerProfileConfiguration> current,
        IReadOnlyList<LanguageServerProfileConfiguration> updates)
    {
        var merged = current.ToDictionary(static profile => profile.Id, StringComparer.Ordinal);
        foreach (var profile in updates)
        {
            merged[profile.Id] = profile;
        }

        return [.. merged.Values.OrderBy(static profile => profile.Id, StringComparer.Ordinal)];
    }
}
