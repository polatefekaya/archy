namespace Archy.Features.Analysis.ConfigurationKeys;

public static class ConfigurationKeyPath
{
    public static string StableId(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        return $"configuration:key:{keyPath}";
    }

    public static bool TryCombine(string prefix, string rawKey, out string keyPath)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(rawKey);

        var normalizedRawKey = rawKey.Trim().Replace("__", ":", StringComparison.Ordinal);
        var segments = normalizedRawKey.Split(':', StringSplitOptions.TrimEntries);
        if (segments.Length == 0 ||
            segments.Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            keyPath = string.Empty;
            return false;
        }

        keyPath = prefix.Length == 0 ? string.Join(':', segments) : string.Concat(prefix, ":", string.Join(':', segments));
        return true;
    }
}
