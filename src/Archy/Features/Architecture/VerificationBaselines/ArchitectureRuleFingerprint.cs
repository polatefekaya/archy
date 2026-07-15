using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Architecture.VerificationBaselines;

/// <summary>Hashes only architecture-rule semantics so unrelated configuration cannot invalidate accepted debt.</summary>
public static partial class ArchitectureRuleFingerprint
{
    public static string Calculate(
        IReadOnlyList<LayerRuleConfiguration> layers,
        ArchitectureEnforcementConfiguration enforcement)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(enforcement);
        var canonical = new ArchitectureRuleFingerprintInput(
            [.. layers
                .OrderBy(static layer => layer.Name, StringComparer.Ordinal)
                .Select(static layer => new LayerRuleConfiguration(
                    layer.Name,
                    [.. layer.Includes.OrderBy(static include => include, StringComparer.Ordinal)],
                    [.. layer.MayDependOn.OrderBy(static dependency => dependency, StringComparer.Ordinal)]))],
            new ArchitectureEnforcementConfiguration(
                [.. enforcement.HardEdgeKinds.OrderBy(static kind => kind, StringComparer.Ordinal)]));
        var json = JsonSerializer.Serialize(canonical, ArchitectureRuleFingerprintJsonContext.Default.ArchitectureRuleFingerprintInput);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private sealed record ArchitectureRuleFingerprintInput(
        LayerRuleConfiguration[] Layers,
        ArchitectureEnforcementConfiguration Enforcement);

    [JsonSerializable(typeof(ArchitectureRuleFingerprintInput))]
    private sealed partial class ArchitectureRuleFingerprintJsonContext : JsonSerializerContext;
}
