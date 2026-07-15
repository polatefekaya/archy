using System.Text.Json;
using Archy.Features.Analysis.ProviderSiteMatches;

namespace Archy.UnitTests.Features.Analysis.ProviderSiteMatches;

public sealed class ProviderSiteMatchContractTests
{
    [Fact]
    public void FakeMatcherPayloadRoundTripsThroughTheNativeAotJsonContract()
    {
        var matched = ProviderSiteMatchContract.Create(
            providerId: "dotnet-configuration",
            language: "csharp",
            framework: "Microsoft.Extensions.Configuration",
            shape: "GetValue<T>(string)",
            evidence: new ProviderSiteEvidence("src/Settings.cs", "ABC123", 12, 17, 12, 65),
            captures:
            [
                new ProviderSiteCapture("key", ProviderSiteCaptureKind.Literal, "ConnectionStrings:Primary"),
                new ProviderSiteCapture("valueType", ProviderSiteCaptureKind.Type, "string"),
            ],
            state: ProviderSiteMatchState.Matched,
            diagnostic: null);
        Assert.True(matched.IsSuccess);

        var payload = JsonSerializer.Serialize(
            new[] { matched.Value },
            ProviderSiteMatchJsonContext.Default.ProviderSiteMatchArray);
        var restored = JsonSerializer.Deserialize(
            payload,
            ProviderSiteMatchJsonContext.Default.ProviderSiteMatchArray);

        var match = Assert.Single(restored!);
        Assert.Equal(matched.Value.SchemaVersion, match.SchemaVersion);
        Assert.Equal(matched.Value.ProviderId, match.ProviderId);
        Assert.Equal(matched.Value.Language, match.Language);
        Assert.Equal(matched.Value.Framework, match.Framework);
        Assert.Equal(matched.Value.Shape, match.Shape);
        Assert.Equal(matched.Value.Evidence, match.Evidence);
        Assert.Equal(matched.Value.State, match.State);
        Assert.Null(match.Diagnostic);
        Assert.Null(ProviderSiteMatchContract.Validate(match));
        Assert.Equal(matched.Value.Captures, match.Captures);
    }

    [Theory]
    [InlineData(ProviderSiteMatchState.Unresolved)]
    [InlineData(ProviderSiteMatchState.Unsupported)]
    [InlineData(ProviderSiteMatchState.Degraded)]
    public void DiagnosticStatesRequireAConcreteDiagnostic(ProviderSiteMatchState state)
    {
        var result = ProviderSiteMatchContract.Create(
            providerId: "fixture",
            language: "csharp",
            framework: null,
            shape: "fixture",
            evidence: new ProviderSiteEvidence("src/Fixture.cs", "ABC123", 1, 1, 1, 7),
            captures: [],
            state,
            diagnostic: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("validation", result.Problem!.Code);
    }

    [Fact]
    public void ContractRejectsDuplicateCaptureNamesAndAbsoluteSourcePaths()
    {
        var duplicateCapture = ProviderSiteMatchContract.Create(
            providerId: "fixture",
            language: "csharp",
            framework: null,
            shape: "fixture",
            evidence: new ProviderSiteEvidence("src/Fixture.cs", "ABC123", 1, 1, 1, 7),
            captures:
            [
                new ProviderSiteCapture("value", ProviderSiteCaptureKind.Literal, "one"),
                new ProviderSiteCapture("value", ProviderSiteCaptureKind.Literal, "two"),
            ],
            state: ProviderSiteMatchState.Matched,
            diagnostic: null);
        var absolutePath = ProviderSiteMatchContract.Create(
            providerId: "fixture",
            language: "csharp",
            framework: null,
            shape: "fixture",
            evidence: new ProviderSiteEvidence(Path.GetTempPath(), "ABC123", 1, 1, 1, 7),
            captures: [],
            state: ProviderSiteMatchState.Matched,
            diagnostic: null);

        Assert.False(duplicateCapture.IsSuccess);
        Assert.False(absolutePath.IsSuccess);
    }
}
