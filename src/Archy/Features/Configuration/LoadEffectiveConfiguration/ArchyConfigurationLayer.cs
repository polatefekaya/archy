namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed record ArchyConfigurationLayer(
    string? WorkspaceDisplayName,
    string? LocalStateRootPath,
    string? CSharpLanguageServerCommand,
    string[]? CSharpLanguageServerArguments,
    bool? DependencyInjectionProviderEnabled,
    bool? MessagingProviderEnabled,
    bool? EntityFrameworkCoreProviderEnabled,
    bool? CacheProviderEnabled,
    bool? ConfigurationProviderEnabled,
    LayerRuleConfiguration[]? Layers,
    string? ModelProvider,
    string? SummaryModel,
    string? EmbeddingModel,
    int? MaxRequestsPerRun,
    int? MaxTokensPerRun,
    string? JscpdCommand,
    string? LouvainCommand,
    string[]? ScopeInclude,
    string[]? ScopeExclude,
    double? ArchitectureWeight,
    double? DuplicatesWeight,
    double? DocumentationWeight,
    double? DecisionsWeight)
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
            CSharpLanguageServer = current.CSharpLanguageServer with
            {
                Command = CSharpLanguageServerCommand ?? current.CSharpLanguageServer.Command,
                Arguments = CSharpLanguageServerArguments ?? current.CSharpLanguageServer.Arguments,
            },
            Providers = current.Providers with
            {
                DependencyInjection = DependencyInjectionProviderEnabled ?? current.Providers.DependencyInjection,
                Messaging = MessagingProviderEnabled ?? current.Providers.Messaging,
                EntityFrameworkCore = EntityFrameworkCoreProviderEnabled ?? current.Providers.EntityFrameworkCore,
                Cache = CacheProviderEnabled ?? current.Providers.Cache,
                Configuration = ConfigurationProviderEnabled ?? current.Providers.Configuration,
            },
            Layers = Layers ?? current.Layers,
            Model = current.Model with
            {
                Provider = ModelProvider ?? current.Model.Provider,
                SummaryModel = SummaryModel ?? current.Model.SummaryModel,
                EmbeddingModel = EmbeddingModel ?? current.Model.EmbeddingModel,
                MaxRequestsPerRun = MaxRequestsPerRun ?? current.Model.MaxRequestsPerRun,
                MaxTokensPerRun = MaxTokensPerRun ?? current.Model.MaxTokensPerRun,
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
        };
    }

    private static string ResolvePath(string configuredPath, string sourcePath)
    {
        return Path.IsPathFullyQualified(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, configuredPath));
    }
}
