using Archy.Features.Analysis.CSharpPatternTables;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.UnitTests.Features.Analysis.CSharpPatternTables;

public sealed class CSharpPatternTableLoaderTests
{
    [Fact]
    public void LoadIncludesVersionedBuiltInsAndConfiguredInvocationPattern()
    {
        var configuration = ArchyConfiguration.Default with
        {
            ProviderPatterns =
            [
                new ProviderPatternConfiguration(
                    "custom-domain-publish",
                    "MyCompany.Messaging",
                    "invocation",
                    "Publish",
                    "MyCompany.Messaging.IDomainBus",
                    "message",
                    0),
            ],
        };

        var result = new CSharpPatternTableLoader().Load(configuration);

        Assert.True(result.IsSuccess);
        Assert.Equal(CSharpPatternTableLoader.SchemaVersion, result.Value.SchemaVersion);
        var custom = Assert.Single(result.Value.Patterns, static pattern => pattern.Id == "custom-domain-publish");
        Assert.Equal(CSharpPatternMatchKind.Invocation, custom.MatchKind);
        Assert.Equal("Publish", custom.Member);
        Assert.Equal("message", custom.Capture!.Name);
        Assert.Equal(0, custom.Capture.ArgumentIndex);
        Assert.Contains(result.Value.Patterns, static pattern => pattern.Id == "dotnet-di-add-scoped");
    }

    [Fact]
    public void LoadRejectsConfiguredPatternThatCollidesWithABuiltIn()
    {
        var configuration = ArchyConfiguration.Default with
        {
            ProviderPatterns =
            [
                new ProviderPatternConfiguration(
                    "dotnet-message-publish",
                    "Example",
                    "invocation",
                    "Publish",
                    null,
                    null,
                    null),
            ],
        };

        var result = new CSharpPatternTableLoader().Load(configuration);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
    }
}
