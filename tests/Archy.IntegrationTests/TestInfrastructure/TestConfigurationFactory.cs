using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.IntegrationTests.TestInfrastructure;

internal static class TestConfigurationFactory
{
    public static ArchyConfigurationLoader CreateLoader(string? userConfigurationPath = null) => new(
        new FixedUserConfigurationPathProvider(userConfigurationPath),
        new TomlConfigurationParser());

    private sealed class FixedUserConfigurationPathProvider(string? path) : IUserConfigurationPathProvider
    {
        public string? GetPath() => path;
    }
}
