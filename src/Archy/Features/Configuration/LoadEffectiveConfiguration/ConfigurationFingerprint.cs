using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public static partial class ConfigurationFingerprint
{
    public static string Calculate(ArchyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var json = JsonSerializer.Serialize(configuration, ConfigurationFingerprintJsonContext.Default.ArchyConfiguration);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    [JsonSerializable(typeof(ArchyConfiguration))]
    private sealed partial class ConfigurationFingerprintJsonContext : JsonSerializerContext;
}
