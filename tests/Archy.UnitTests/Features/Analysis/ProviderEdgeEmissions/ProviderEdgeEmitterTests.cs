using System.Text.Json;
using Archy.Features.Analysis.ProviderEdgeEmissions;
using Archy.Features.Analysis.ProviderJoinKeys;
using Archy.Features.Analysis.ProviderSiteMatches;

namespace Archy.UnitTests.Features.Analysis.ProviderEdgeEmissions;

public sealed class ProviderEdgeEmitterTests
{
    [Fact]
    public void EmitBuildsAProvenGraphFactWithDeterministicJoinIdentity()
    {
        var result = new ProviderEdgeEmitter().Emit(CreateIntent());

        Assert.True(result.IsSuccess);
        var edge = result.Value;
        Assert.Equal("configuration_read", edge.EdgeKind);
        Assert.Equal("dotnet-configuration", edge.Provider);
        Assert.Equal(0.8, edge.Confidence);
        Assert.Equal("configuration_path:Feature:ApiKey", edge.NormalizedJoinKey);
        using var evidence = JsonDocument.Parse(edge.EvidenceJson);
        Assert.Equal("static IConfiguration GetValue call", evidence.RootElement.GetProperty("rationale").GetString());
        Assert.Equal("ExplicitSyntax", evidence.RootElement.GetProperty("confidenceTier").GetString());
        Assert.Equal("Feature:ApiKey", evidence.RootElement.GetProperty("joinKey").GetProperty("normalizedValue").GetString());
    }

    [Fact]
    public void EmitRejectsUnresolvedSitesAndMissingRationale()
    {
        var unresolved = CreateIntent() with
        {
            Site = CreateSite(
                ProviderSiteMatchState.Unresolved,
                new ProviderSiteMatchDiagnostic("dynamic_key", "The key expression is dynamic.")),
        };
        var missingRationale = CreateIntent() with { Rationale = " " };
        var emitter = new ProviderEdgeEmitter();

        var unresolvedResult = emitter.Emit(unresolved);
        var missingRationaleResult = emitter.Emit(missingRationale);

        Assert.False(unresolvedResult.IsSuccess);
        Assert.False(missingRationaleResult.IsSuccess);
        Assert.Equal("validation", unresolvedResult.Problem!.Code);
        Assert.Equal("validation", missingRationaleResult.Problem!.Code);
    }

    private static ProviderEdgeEmissionIntent CreateIntent() => new(
        "configuration_read",
        "dotnet-configuration",
        "csharp:type:src/Settings.cs:Sample.Settings",
        "configuration:key:Feature:ApiKey",
        ProviderConfidenceTier.ExplicitSyntax,
        "static IConfiguration GetValue call",
        CreateSite(ProviderSiteMatchState.Matched, diagnostic: null),
        CreateConfigurationJoinKey());

    private static ProviderSiteMatch CreateSite(
        ProviderSiteMatchState state,
        ProviderSiteMatchDiagnostic? diagnostic)
    {
        var result = ProviderSiteMatchContract.Create(
            "dotnet-configuration",
            "csharp",
            "Microsoft.Extensions.Configuration",
            "GetValue<T>(string)",
            new ProviderSiteEvidence("src/Settings.cs", "ABC123", 12, 17, 12, 65),
            [new ProviderSiteCapture("key", ProviderSiteCaptureKind.Literal, "Feature:ApiKey")],
            state,
            diagnostic);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ProviderJoinKey CreateConfigurationJoinKey()
    {
        var result = ProviderJoinKeyContract.Create(
            "key",
            ProviderJoinStrategy.ConfigurationPath,
            "Feature__ApiKey",
            "Feature:ApiKey",
            new ProviderJoinKeyNormalization(
                "configuration-path/v1",
                [new ProviderJoinKeyNormalizationStep("separator", "__=>:")]));
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
