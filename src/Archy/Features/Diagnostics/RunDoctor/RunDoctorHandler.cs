using Archy.Features.Configuration.LoadEffectiveConfiguration;
using Archy.Features.Diagnostics.ReadConfiguredLanguages;
using Archy.Features.Graph.ReadGraphRevision;
using Archy.Features.Storage.WorkspaceDatabase.Check;
using Archy.Features.Integrations.GitHooks;
using Archy.Features.Duplicates.EmbeddingCache;
using Archy.Features.Workspaces.InitializeWorkspace;
using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;
using Mediator;

namespace Archy.Features.Diagnostics.RunDoctor;

/// <summary>Read-only readiness report. It never initializes state, migrates a database, or starts analysis/LSP/model work.</summary>
public sealed class RunDoctorHandler(
    IWorkspaceLocator workspaceLocator,
    IArchyConfigurationLoader configurationLoader,
    IWorkspaceStateLayout stateLayout,
    IGraphRevisionSnapshotReader graphReader,
    WorkspaceDatabaseChecker databaseChecker,
    IGitHookLifecycle gitHooks,
    IEmbeddingCacheRepository embeddingCache,
    IRequestHandler<ReadConfiguredLanguagesQuery, Result<IReadOnlyList<ConfiguredLanguageProfileStatus>>> languageReader)
    : IRequestHandler<RunDoctorQuery, Result<DoctorReport>>
{
    public async ValueTask<Result<DoctorReport>> Handle(RunDoctorQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var checks = new List<DoctorCheck>();
        var workspace = workspaceLocator.Locate(query.Path);
        if (!workspace.IsSuccess) return ResultFactory.Success(Build([new("workspace.repository", DoctorSeverity.Error, workspace.Problem!.Message, "Run this command from a Git repository.")]));
        checks.Add(new("workspace.repository", DoctorSeverity.Info, $"Repository: {workspace.Value.RepositoryRoot}", null));
        var configuration = await configurationLoader.LoadAsync(workspace.Value, query.ConfigurationPath, query.StateRoot, cancellationToken);
        if (!configuration.IsSuccess) return ResultFactory.Success(Build([.. checks, new("configuration.effective", DoctorSeverity.Error, configuration.Problem!.Message, "Correct the configuration and run doctor again.")]));
        checks.Add(new("configuration.effective", DoctorSeverity.Info, $"Effective configuration loaded from {configuration.Value!.Sources.Count(source => source.WasApplied)} source(s).", null));
        var location = stateLayout.Resolve(workspace.Value, configuration.Value.StateRoot);
        if (!location.IsSuccess) return ResultFactory.Success(Build([.. checks, new("workspace.state", DoctorSeverity.Error, location.Problem!.Message, "Provide a valid --state-root.")]));
        checks.Add(File.Exists(location.Value.ManifestPath)
            ? new("workspace.state", DoctorSeverity.Info, "Workspace manifest is present.", null)
            : new("workspace.state", DoctorSeverity.Warning, "No initialized workspace manifest exists.", "Run 'archy workspace init' before analysis."));
        if (!File.Exists(location.Value.DatabasePath))
        {
            checks.Add(new("workspace.database", DoctorSeverity.Warning, "No workspace database exists.", "Run 'archy workspace init' before analysis."));
            checks.Add(new("analysis.graph", DoctorSeverity.Warning, "No active graph is available because the database is absent.", "Run 'archy analyze' after initialization."));
        }
        else
        {
            var integrity = await databaseChecker.CheckAsync(location.Value, cancellationToken);
            if (!integrity.IsSuccess)
                checks.Add(new("workspace.database", DoctorSeverity.Error, integrity.Problem!.Message, "Run 'archy db check' and repair or restore local workspace state."));
            else if (!integrity.Value!.IsHealthy)
                checks.Add(new("workspace.database", DoctorSeverity.Error, "Workspace database integrity checks reported failures.", "Run 'archy db check --json' and repair or restore local workspace state."));
            else
                checks.Add(new("workspace.database", DoctorSeverity.Info, $"Workspace database is healthy at schema version {integrity.Value.SchemaVersion}.", null));
            var graph = await graphReader.ReadActiveAsync(location.Value, cancellationToken);
            if (!graph.IsSuccess) checks.Add(new("analysis.graph", DoctorSeverity.Error, graph.Problem!.Message, "Run 'archy db check' and repair local workspace state."));
            else if (graph.Value is null) checks.Add(new("analysis.graph", DoctorSeverity.Warning, "No active graph revision exists.", "Run 'archy analyze'."));
            else checks.Add(new("analysis.graph", DoctorSeverity.Info, $"Active graph revision {graph.Value.Revision} has {graph.Value.Nodes.Count} nodes.", null));
        }
        var languages = await languageReader.Handle(new(query.Path, query.ConfigurationPath, query.StateRoot), cancellationToken);
        if (!languages.IsSuccess) checks.Add(new("languages.inventory", DoctorSeverity.Error, languages.Problem!.Message, "Correct language profile configuration."));
        else foreach (var language in languages.Value!) checks.Add(new($"languages.{language.Id}", ToSeverity(language.Readiness), language.Detail, language.Remediation));
        var hooks = await gitHooks.InspectAsync(workspace.Value.RepositoryRoot, cancellationToken);
        if (!hooks.IsSuccess) checks.Add(new("integrations.git_hooks", DoctorSeverity.Warning, hooks.Problem!.Message, "Run 'archy hooks status' to inspect Git hook availability."));
        else if (hooks.Value!.Hooks.All(hook => hook.IsManagedByArchy)) checks.Add(new("integrations.git_hooks", DoctorSeverity.Info, "Archy pre-commit and pre-push hooks are installed.", null));
        else checks.Add(new("integrations.git_hooks", DoctorSeverity.Warning, "One or more Archy Git delivery hooks are not installed.", "Run 'archy hooks install --path .'."));
        checks.Add(new("verification.architecture", DoctorSeverity.Info, "Verification readiness is advisory; doctor does not run verification.", "Run 'archy verify --path .' after analysis."));
        checks.Add(new("integrations.codex_mcp", DoctorSeverity.Info, "MCP is available through 'archy mcp stdio'.", null));
        var embeddingModel = configuration.Value.Configuration.Model.EmbeddingModel;
        if (embeddingModel is null)
        {
            checks.Add(new("embeddings.configuration", DoctorSeverity.Info, "No embedding model is configured; structural similarity remains available.", null));
            checks.Add(new("embeddings.credential", DoctorSeverity.Info, "No embedding credential is required because embeddings are not configured.", null));
            checks.Add(new("embeddings.cache", DoctorSeverity.Info, "No embedding cache is selected because embeddings are not configured.", null));
        }
        else
        {
            checks.Add(new("embeddings.configuration", DoctorSeverity.Info, $"Embedding model '{embeddingModel}' is configured; this report never exposes vectors.", null));
            var keyAvailable = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
            checks.Add(keyAvailable
                ? new("embeddings.credential", DoctorSeverity.Info, "An embedding credential is available through the supported environment boundary; its value is not inspected or emitted.", null)
                : new("embeddings.credential", DoctorSeverity.Warning, "An embedding model is configured but no supported environment credential is available.", "Set OPENAI_API_KEY in the process environment when embedding indexing is intended."));
            if (!File.Exists(location.Value.DatabasePath)) checks.Add(new("embeddings.cache", DoctorSeverity.Info, "No workspace database exists, so no embedding cache is available.", null));
            else
            {
                var cache = await embeddingCache.ReadStatisticsAsync(location.Value, embeddingModel, cancellationToken);
                if (!cache.IsSuccess) checks.Add(new("embeddings.cache", DoctorSeverity.Warning, cache.Problem!.Message, "Run 'archy embeddings status --json' after verifying local workspace state."));
                else checks.Add(new("embeddings.cache", DoctorSeverity.Info, $"Embedding cache has {cache.Value!.Count} entry(s) for the configured model across {cache.Value.Dimensions.Count} vector dimension(s); vectors are not emitted.", null));
            }
        }
        return ResultFactory.Success(Build(checks));
    }

    private static DoctorReport Build(IReadOnlyList<DoctorCheck> checks)
    {
        var ordered = checks.OrderBy(check => check.Id, StringComparer.Ordinal).ToArray();
        return new(ordered, ordered.Count(check => check.Severity == DoctorSeverity.Error), ordered.Count(check => check.Severity == DoctorSeverity.Warning), ordered.Count(check => check.Severity == DoctorSeverity.Info));
    }
    private static DoctorSeverity ToSeverity(LanguageProfileReadiness readiness) => readiness switch { LanguageProfileReadiness.CommandUnavailable or LanguageProfileReadiness.InvalidConfiguration => DoctorSeverity.Error, LanguageProfileReadiness.Ready => DoctorSeverity.Info, _ => DoctorSeverity.Warning };
}
