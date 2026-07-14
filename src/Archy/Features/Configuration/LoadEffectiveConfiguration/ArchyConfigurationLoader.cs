using Archy.Features.Workspaces.LocateWorkspace;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed class ArchyConfigurationLoader(
    IUserConfigurationPathProvider userConfigurationPathProvider,
    ITomlConfigurationParser parser)
    : IArchyConfigurationLoader
{
    public async ValueTask<Result<EffectiveConfiguration>> LoadAsync(
        LocatedWorkspace workspace,
        string? explicitConfigurationPath,
        string? stateRootOverride,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        var configuration = ArchyConfiguration.Default;
        var sources = new List<ConfigurationSource>
        {
            new(ConfigurationSourceKind.Defaults, null, true),
        };

        var userPath = userConfigurationPathProvider.GetPath();
        var userConfiguration = await LoadOptionalAsync(
            ConfigurationSourceKind.User,
            userPath,
            cancellationToken);
        if (!userConfiguration.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(userConfiguration.Problem!);
        }

        var applyUser = ApplyLayer(configuration, sources, userConfiguration.Value);
        if (!applyUser.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(applyUser.Problem!);
        }

        configuration = applyUser.Value;

        var repositoryPath = Path.Combine(workspace.RepositoryRoot, "archy.toml");
        var repositoryConfiguration = await LoadOptionalAsync(
            ConfigurationSourceKind.Repository,
            repositoryPath,
            cancellationToken);
        if (!repositoryConfiguration.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(repositoryConfiguration.Problem!);
        }

        var applyRepository = ApplyLayer(configuration, sources, repositoryConfiguration.Value);
        if (!applyRepository.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(applyRepository.Problem!);
        }

        configuration = applyRepository.Value;

        var explicitConfiguration = await LoadExplicitAsync(explicitConfigurationPath, cancellationToken);
        if (!explicitConfiguration.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(explicitConfiguration.Problem!);
        }

        var applyExplicit = ApplyLayer(configuration, sources, explicitConfiguration.Value);
        if (!applyExplicit.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(applyExplicit.Problem!);
        }

        configuration = applyExplicit.Value;

        var stateRoot = string.IsNullOrWhiteSpace(stateRootOverride)
            ? configuration.LocalState.RootPath
            : stateRootOverride;
        if (!string.IsNullOrWhiteSpace(stateRootOverride))
        {
            sources.Add(new ConfigurationSource(ConfigurationSourceKind.CommandLine, null, true));
        }

        var validation = ValidateEffectiveConfiguration(configuration);
        if (!validation.IsSuccess)
        {
            return ResultFactory.Failure<EffectiveConfiguration>(validation.Problem!);
        }

        return ResultFactory.Success(new EffectiveConfiguration(configuration, stateRoot, [.. sources]));
    }

    private static Result<ArchyConfiguration> ApplyLayer(
        ArchyConfiguration current,
        List<ConfigurationSource> sources,
        LoadedConfigurationLayer? loadedLayer)
    {
        if (loadedLayer is null)
        {
            return ResultFactory.Success(current);
        }

        if (loadedLayer.Kind == ConfigurationSourceKind.Repository && loadedLayer.Layer.LocalStateRootPath is not null)
        {
            return ResultFactory.Failure<ArchyConfiguration>(
                Problem.Validation(
                    $"{loadedLayer.Path}: storage.state_root is only permitted in user or explicitly selected configuration; repository configuration cannot redirect local state."));
        }

        try
        {
            var merged = loadedLayer.Layer.ApplyTo(current, loadedLayer.Path);
            sources.Add(new ConfigurationSource(loadedLayer.Kind, loadedLayer.Path, true));
            return ResultFactory.Success(merged);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ResultFactory.Failure<ArchyConfiguration>(
                Problem.Validation($"{loadedLayer.Path}: local-state path is invalid: {exception.Message}"));
        }
    }

    private async ValueTask<Result<LoadedConfigurationLayer?>> LoadOptionalAsync(
        ConfigurationSourceKind kind,
        string? candidatePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return ResultFactory.Success<LoadedConfigurationLayer?>(null);
        }

        var canonicalPath = CanonicalizePath(candidatePath);
        if (!canonicalPath.IsSuccess)
        {
            return ResultFactory.Failure<LoadedConfigurationLayer?>(canonicalPath.Problem!);
        }

        if (!File.Exists(canonicalPath.Value))
        {
            return ResultFactory.Success<LoadedConfigurationLayer?>(null);
        }

        return await ReadAndParseAsync(kind, canonicalPath.Value, cancellationToken);
    }

    private async ValueTask<Result<LoadedConfigurationLayer?>> LoadExplicitAsync(
        string? explicitConfigurationPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(explicitConfigurationPath))
        {
            return ResultFactory.Success<LoadedConfigurationLayer?>(null);
        }

        var canonicalPath = CanonicalizePath(explicitConfigurationPath);
        if (!canonicalPath.IsSuccess)
        {
            return ResultFactory.Failure<LoadedConfigurationLayer?>(canonicalPath.Problem!);
        }

        if (!File.Exists(canonicalPath.Value))
        {
            return ResultFactory.Failure<LoadedConfigurationLayer?>(
                Problem.NotFound($"Explicit configuration file '{canonicalPath.Value}' does not exist."));
        }

        return await ReadAndParseAsync(ConfigurationSourceKind.Explicit, canonicalPath.Value, cancellationToken);
    }

    private async ValueTask<Result<LoadedConfigurationLayer?>> ReadAndParseAsync(
        ConfigurationSourceKind kind,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await File.ReadAllTextAsync(path, cancellationToken);
            var parsed = parser.Parse(document, path);
            if (!parsed.IsSuccess)
            {
                return ResultFactory.Failure<LoadedConfigurationLayer?>(parsed.Problem!);
            }

            return ResultFactory.Success<LoadedConfigurationLayer?>(
                new LoadedConfigurationLayer(kind, path, parsed.Value));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ResultFactory.Failure<LoadedConfigurationLayer?>(
                Problem.Storage($"Archy could not read configuration '{path}': {exception.Message}"));
        }
    }

    private static Result<string> CanonicalizePath(string path)
    {
        try
        {
            return ResultFactory.Success(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ResultFactory.Failure<string>(
                Problem.Validation($"Configuration path '{path}' is invalid: {exception.Message}"));
        }
    }

    private static Result<ArchyConfiguration> ValidateEffectiveConfiguration(ArchyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.CSharpLanguageServer.Command is null && configuration.CSharpLanguageServer.Arguments.Length > 0)
        {
            return ResultFactory.Failure<ArchyConfiguration>(
                Problem.Validation("language_servers.csharp.args requires language_servers.csharp.command."));
        }

        if (configuration.HealthWeights.Architecture +
            configuration.HealthWeights.Duplicates +
            configuration.HealthWeights.Documentation +
            configuration.HealthWeights.Decisions <= 0)
        {
            return ResultFactory.Failure<ArchyConfiguration>(
                Problem.Validation("health_weights must have a positive total."));
        }

        return ResultFactory.Success(configuration);
    }

    private sealed record LoadedConfigurationLayer(
        ConfigurationSourceKind Kind,
        string Path,
        ArchyConfigurationLayer Layer);
}
