using Archy.Features.Workspaces.LocateWorkspace;
using Archy.IntegrationTests.TestInfrastructure;

namespace Archy.IntegrationTests.Features.Configuration;

public sealed class EffectiveConfigurationTests
{
    [Fact]
    public async Task LoadUsesTheBuiltInCsharpLanguageServerProfileWhenNoProfileIsConfigured()
    {
        using var fixture = TemporaryRepository.Create();

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var profile = Assert.Single(result.Value.Configuration.LanguageServerProfiles, static profile => profile.Id == "csharp");
        Assert.Equal("csharp", profile.Id);
        Assert.Equal("csharp", profile.LanguageId);
        Assert.Equal("Microsoft.CodeAnalysis.LanguageServer", profile.Command);
        Assert.Equal(["--stdio"], profile.Arguments);
    }

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

    [Fact]
    public async Task LoadAcceptsRepositoryLevelDeclarativeProviderPatterns()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [[provider_patterns]]
            id = "custom-domain-publish"
            framework = "MyCompany.Messaging"
            match_kind = "invocation"
            member = "Publish"
            type = "MyCompany.Messaging.IDomainBus"
            capture_name = "message"
            capture_argument_index = 0
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var pattern = Assert.Single(result.Value.Configuration.ProviderPatterns);
        Assert.Equal("custom-domain-publish", pattern.Id);
        Assert.Equal(0, pattern.CaptureArgumentIndex);
    }

    [Fact]
    public async Task LoadAcceptsADeclarativeNonCsharpLanguageServerProfile()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [[language_server_profiles]]
            id = "typescript"
            language_id = "typescript"
            extensions = [".ts", ".tsx"]
            markers = ["package.json", "tsconfig.json"]
            command = "typescript-language-server"
            args = ["--stdio"]
            symbol_identity_prefix = "ts"
            max_symbol_queries = 7

            [language_server_profiles.symbol_kinds]
            namespace = [3]
            type = [5, 23]
            method = [6]
            property = [7]
            field = [8]
            event = [24]
            parameter = [26]
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var profile = Assert.Single(result.Value.Configuration.LanguageServerProfiles, static profile => profile.Id == "typescript");
        Assert.Equal("typescript", profile.Id);
        Assert.Equal("typescript", profile.LanguageId);
        Assert.Equal([".ts", ".tsx"], profile.Extensions);
        Assert.Equal("ts", profile.SymbolIdentityPrefix);
        Assert.Equal(7, profile.MaxSymbolQueries);
        Assert.Contains(profile.SymbolKinds, static mapping => mapping.SemanticKind == "type" && mapping.LspKinds.SequenceEqual([5, 23]));
        Assert.Contains(result.Value.Configuration.LanguageServerProfiles, static profile => profile.Id == "csharp");
    }

    [Fact]
    public async Task LoadRejectsProfilesThatMapTheSameLspSymbolKindTwice()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [[language_server_profiles]]
            id = "invalid"
            language_id = "invalid"
            extensions = [".invalid"]
            command = "invalid-lsp"
            symbol_identity_prefix = "invalid"

            [language_server_profiles.symbol_kinds]
            type = [5]
            method = [5]
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
    public async Task LoadRejectsLanguageServerProfileQueryLimitsOutsideTheSafeBound()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [[language_server_profiles]]
            id = "invalid-limit"
            language_id = "invalid"
            extensions = [".invalid"]
            command = "invalid-lsp"
            symbol_identity_prefix = "invalid"
            max_symbol_queries = 100001

            [language_server_profiles.symbol_kinds]
            type = [5]
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("max_symbol_queries", result.Problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAcceptsForwardLayerDependenciesWithRepositoryRelativePatterns()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [[layers]]
            name = "Presentation"
            include = ["src/**/Presentation/**"]
            may_depend_on = ["Application"]

            [[layers]]
            name = "Application"
            include = ["src/**/Application/**"]
            may_depend_on = []

            [enforcement]
            hard_edge_kinds = ["calls", "inherits"]
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
        var presentation = Assert.Single(result.Value.Configuration.Layers, static layer => layer.Name == "Presentation");
        Assert.Equal(["Application"], presentation.MayDependOn);
        Assert.Equal(["calls", "inherits"], result.Value.Configuration.Enforcement.HardEdgeKinds);
    }

    [Theory]
    [InlineData("../outside/**", "Application", "repository-relative")]
    [InlineData("src/**", "Unknown", "undeclared layer")]
    [InlineData("src/**", "Presentation", "other than itself")]
    public async Task LoadRejectsUnsoundArchitectureLayerRules(string include, string dependency, string expectedMessage)
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            $"""
            schema_version = 1

            [[layers]]
            name = "Presentation"
            include = ["{include}"]
            may_depend_on = ["{dependency}"]
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(expectedMessage, result.Problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadRejectsNonDeterministicHardEdgeConfiguration()
    {
        using var fixture = TemporaryRepository.Create();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "archy.toml"),
            """
            schema_version = 1

            [enforcement]
            hard_edge_kinds = ["calls", "calls"]
            """);

        var result = await TestConfigurationFactory.CreateLoader().LoadAsync(
            await LocateWorkspaceAsync(fixture),
            explicitConfigurationPath: null,
            stateRootOverride: null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("hard_edge_kinds", result.Problem!.Message, StringComparison.Ordinal);
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
