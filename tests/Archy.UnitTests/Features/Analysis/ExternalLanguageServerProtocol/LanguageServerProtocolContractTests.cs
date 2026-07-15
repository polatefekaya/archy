using System.Text.Json;
using Archy.Features.Analysis.ExternalLanguageServerProtocol;

namespace Archy.UnitTests.Features.Analysis.ExternalLanguageServerProtocol;

public sealed class LanguageServerProtocolContractTests
{
    [Fact]
    public void InitializeRequestUsesVersionedJsonRpcAndSnapshotContract()
    {
        var specification = Specification();

        var request = LanguageServerProtocolContract.CreateInitializeRequest(specification, 42);

        Assert.True(request.IsSuccess);
        Assert.Equal("2.0", request.Value.JsonRpc);
        Assert.Equal(1, request.Value.Id);
        Assert.Equal("initialize", request.Value.Method);
        Assert.Equal("file:///repo/", request.Value.Parameters.RootUri);
        Assert.Equal(LanguageServerProtocolContract.CurrentSchemaVersion, request.Value.Parameters.InitializationOptions.ProtocolSchemaVersion);
        Assert.Equal("language-semantic/v2", request.Value.Parameters.InitializationOptions.SemanticContractSchemaVersion);

        var json = JsonSerializer.Serialize(request.Value, LanguageServerProtocolJsonContext.Default.LspInitializeRequest);
        Assert.Contains("\"jsonRpc\":\"2.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"params\"", json, StringComparison.Ordinal);
        Assert.Contains("\"initializationOptions\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchSpecificationRejectsUnsafeOrUnboundedProcessConfiguration()
    {
        var invalid = Specification() with
        {
            RepositoryRoot = "relative-root",
            Arguments = ["--stdio\0"],
            MaximumMessageBytes = 0,
        };

        Assert.NotNull(LanguageServerProtocolContract.ValidateLaunchSpecification(invalid));
        Assert.False(LanguageServerProtocolContract.CreateInitializeRequest(Specification(), 0).IsSuccess);
    }

    [Fact]
    public void CapabilityNegotiationDegradesOnlyFactsThatDependOnMissingCapabilities()
    {
        var profile = LanguageServerCapabilityNegotiator.Negotiate(
            Specification(),
            new LanguageServerVersionReport("fixture-lsp", "1.2.3"),
            [
                new LanguageServerCapabilityAdvertisement("definition", true, null),
                new LanguageServerCapabilityAdvertisement("references", false, "disabled by fixture"),
            ]);

        Assert.True(profile.Version.WasReported);
        Assert.Equal(LanguageServerCapabilityState.Available, profile.Capabilities.Single(static value => value.Name == "definition").State);
        Assert.Equal(LanguageServerCapabilityState.Degraded, profile.Capabilities.Single(static value => value.Name == "references").State);
    }

    private static LanguageServerLaunchSpecification Specification() => new(
        LanguageServerProtocolContract.CurrentSchemaVersion,
        "fixture-roslyn-lsp",
        "csharp",
        "fixture-lsp",
        ["--stdio"],
        "/repo",
        new LanguageServerTimeouts(5_000, 15_000, 5_000),
        new LanguageServerRestartPolicy(2, 50),
        1_048_576,
        65_536,
        [
            new LanguageServerCapabilityRequirement("definition", true),
            new LanguageServerCapabilityRequirement("references", true),
        ]);
}
