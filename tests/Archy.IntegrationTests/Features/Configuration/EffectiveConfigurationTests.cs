using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Configuration;

public sealed class EffectiveConfigurationTests
{
    [Fact]
    public async Task LoadMergesConfigurationSourcesWithDeterministicPrecedence()
    {
        using var fixture = TemporaryRepository.Create();
        using var configurationDirectory = TemporaryDirectory.Create("config");
        var userConfigurationPath = Path.Combine(configurationDirectory.Path, "user.toml");
        var explicitConfigurationPath = Path.Combine(configurationDirectory.Path, "explicit.toml");

        await File.WriteAllTextAsync(
            userConfigurationPath,
            """
            schema_version = 1

            [storage]
            state_root = "state"

            [model]
            max_requests_per_run = 10

            [providers]
            cache = false
            """);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [model]
            max_tokens_per_run = 1234

            [providers]
            cache = true
            """);
        await File.WriteAllTextAsync(
            explicitConfigurationPath,
            """
            schema_version = 1

            [model]
            provider = "disabled"
            max_requests_per_run = 3
            """);

        var workspace = await LocateWorkspaceAsync(fixture);
        var result = await TestConfigurationFactory.CreateLoader(userConfigurationPath).LoadAsync(
            workspace,
            explicitConfigurationPath,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("disabled", result.Value.Configuration.Model.Provider);
        Assert.Equal(3, result.Value.Configuration.Model.MaxRequestsPerRun);
        Assert.Equal(1234, result.Value.Configuration.Model.MaxTokensPerRun);
        Assert.True(result.Value.Configuration.Providers.Cache);
        Assert.Equal(Path.Combine(configurationDirectory.Path, "state"), result.Value.StateRoot);
        Assert.Equal(4, result.Value.Sources.Length);
    }

    [Fact]
    public async Task LoadRejectsRepositoryControlledLocalStateRedirects()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [storage]
            state_root = "/tmp/unsafe"
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
    }

    [Fact]
    public async Task LoadRejectsPersistedApiKeys()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1
            api_key = "sk-not-allowed"
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("never TOML", result.Problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadRejectsScopePatternsThatEscapeTheRepository()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [scope]
            exclude = ["../outside/**"]
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
    }

    private static async Task<LocatedWorkspace> LocateWorkspaceAsync(TemporaryRepository fixture)
    {
        var result = await fixture.LocateHandler.Handle(
            new LocateWorkspaceCommand(fixture.Root),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
