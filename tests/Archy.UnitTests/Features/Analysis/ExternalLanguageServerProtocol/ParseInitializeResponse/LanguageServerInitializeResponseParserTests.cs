using System.Text.Json;
using Archy.Features.Analysis.ExternalLanguageServerProtocol;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.ParseInitializeResponse;

namespace Archy.UnitTests.Features.Analysis.ExternalLanguageServerProtocol.ParseInitializeResponse;

public sealed class LanguageServerInitializeResponseParserTests
{
    [Fact]
    public void ParsesBooleanAndObjectLspCapabilityAdvertisements()
    {
        using var response = JsonDocument.Parse("""
            {"jsonrpc":"2.0","id":1,"result":{"serverInfo":{"name":"fixture-lsp","version":"1.2.3"},"capabilities":{"documentSymbolProvider":true,"definitionProvider":{},"referencesProvider":false}}}
            """);

        var profile = LanguageServerInitializeResponseParser.Parse(Specification(), response.RootElement);

        Assert.True(profile.IsSuccess);
        Assert.Equal("fixture-lsp", profile.Value.Version.Name);
        Assert.Equal("1.2.3", profile.Value.Version.Version);
        Assert.Equal(LanguageServerCapabilityState.Available, Capability(profile.Value, "documentSymbol").State);
        Assert.Equal(LanguageServerCapabilityState.Available, Capability(profile.Value, "definition").State);
        Assert.Equal(LanguageServerCapabilityState.Degraded, Capability(profile.Value, "references").State);
    }

    [Fact]
    public void RejectsInitializeResponsesWithoutAnObjectCapabilitiesMember()
    {
        using var response = JsonDocument.Parse("""{"jsonrpc":"2.0","id":1,"result":{}}""");

        var profile = LanguageServerInitializeResponseParser.Parse(Specification(), response.RootElement);

        Assert.False(profile.IsSuccess);
        Assert.Equal("validation", profile.Problem!.Code);
    }

    private static LanguageServerCapabilityStatus Capability(LanguageServerCapabilityProfile profile, string name) =>
        Assert.Single(profile.Capabilities, capability => capability.Name == name);

    private static LanguageServerLaunchSpecification Specification() => new(
        LanguageServerProtocolContract.CurrentSchemaVersion,
        "fixture-lsp",
        "csharp",
        "fixture-lsp",
        ["--stdio"],
        "/repo",
        new LanguageServerTimeouts(5_000, 15_000, 5_000),
        new LanguageServerRestartPolicy(2, 50),
        1_048_576,
        65_536,
        [
            new LanguageServerCapabilityRequirement("documentSymbol", true),
            new LanguageServerCapabilityRequirement("definition", true),
            new LanguageServerCapabilityRequirement("references", true),
        ]);
}
