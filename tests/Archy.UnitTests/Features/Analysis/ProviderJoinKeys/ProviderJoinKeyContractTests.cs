using System.Text.Json;
using Archy.Features.Analysis.ProviderJoinKeys;

namespace Archy.UnitTests.Features.Analysis.ProviderJoinKeys;

public sealed class ProviderJoinKeyContractTests
{
    [Fact]
    public void ContractRoundTripsEverySupportedJoinStrategyWithRawAndNormalizedEvidence()
    {
        var keys = new[]
        {
            Create("service", ProviderJoinStrategy.TypeEquality, "global::Sample.IClock", "Sample.IClock", "csharp-symbol/v1", [new("namespace", "Sample")]),
            Create("route", ProviderJoinStrategy.StringEquality, "Order.Created", "order.created", "invariant-lowercase/v1", [new("case", "invariant-lower")]),
            Create("cache", ProviderJoinStrategy.CanonicalPattern, "$\"order:{orderId}\"", "order:{}", "csharp-pattern/v1", [new("placeholder", "orderId")]),
            Create("setting", ProviderJoinStrategy.ConfigurationPath, "Feature__ApiKey", "Feature:ApiKey", "configuration-path/v1", [new("separator", "__=>:")]),
        };

        var payload = JsonSerializer.Serialize(keys, ProviderJoinKeyJsonContext.Default.ProviderJoinKeyArray);
        var restored = JsonSerializer.Deserialize(payload, ProviderJoinKeyJsonContext.Default.ProviderJoinKeyArray);

        Assert.Equal(keys.Length, restored!.Length);
        Assert.Equal(keys.Select(static key => key.Strategy), restored.Select(static key => key.Strategy));
        Assert.Equal(keys.Select(static key => key.RawValue), restored.Select(static key => key.RawValue));
        Assert.Equal(keys.Select(static key => key.NormalizedValue), restored.Select(static key => key.NormalizedValue));
        Assert.All(restored, static key => Assert.Null(ProviderJoinKeyContract.Validate(key)));
    }

    [Fact]
    public void ContractRejectsStrategiesWhoseNormalizationProvenanceIsInsufficient()
    {
        var typeWithoutSymbolIdentity = ProviderJoinKeyContract.Create(
            "service",
            ProviderJoinStrategy.TypeEquality,
            "IClock",
            "IClock",
            new ProviderJoinKeyNormalization("text/v1", []));
        var patternWithoutPlaceholder = ProviderJoinKeyContract.Create(
            "cache",
            ProviderJoinStrategy.CanonicalPattern,
            "order:42",
            "order:{}",
            new ProviderJoinKeyNormalization("csharp-pattern/v1", []));
        var configurationWithoutSeparator = ProviderJoinKeyContract.Create(
            "setting",
            ProviderJoinStrategy.ConfigurationPath,
            "Feature__ApiKey",
            "Feature:ApiKey",
            new ProviderJoinKeyNormalization("configuration-path/v1", []));

        Assert.False(typeWithoutSymbolIdentity.IsSuccess);
        Assert.False(patternWithoutPlaceholder.IsSuccess);
        Assert.False(configurationWithoutSeparator.IsSuccess);
    }

    private static ProviderJoinKey Create(
        string captureName,
        ProviderJoinStrategy strategy,
        string rawValue,
        string normalizedValue,
        string normalizerId,
        IReadOnlyList<ProviderJoinKeyNormalizationStep> steps)
    {
        var result = ProviderJoinKeyContract.Create(
            captureName,
            strategy,
            rawValue,
            normalizedValue,
            new ProviderJoinKeyNormalization(normalizerId, steps));
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
