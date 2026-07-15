using System.Text.Json;

namespace Archy.Features.Sidecars.Protocol;

/// <summary>Rejects incompatible processes before analysis and constrains all sidecar inputs to repository-relative paths.</summary>
public sealed class SidecarProtocolValidator : ISidecarProtocolValidator
{
    public SidecarProtocolFailure? ValidateHandshake(SidecarHandshakeRequest request, SidecarHandshakeResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        if (request.ProtocolVersion != SidecarProtocolContract.Version || response.ProtocolVersion != SidecarProtocolContract.Version)
        {
            return Failure("protocol-version", "The sidecar protocol version is incompatible with this Archy host.");
        }

        if (string.IsNullOrWhiteSpace(request.HostVersion) || string.IsNullOrWhiteSpace(response.SidecarName) || !IsToolVersion(response.ToolVersion) ||
            !AreCapabilitiesComplete(request.RequiredCapabilities) || !AreCapabilitiesComplete(response.Capabilities))
        {
            return Failure("invalid-handshake", "The sidecar handshake is incomplete or malformed.");
        }

        var actual = response.Capabilities.ToDictionary(static capability => capability.Name, StringComparer.Ordinal);
        foreach (var required in request.RequiredCapabilities)
        {
            if (!actual.TryGetValue(required.Name, out var advertised) || advertised.Version < required.Version)
            {
                return Failure("missing-capability", $"Sidecar capability '{required.Name}' version {required.Version} is required.");
            }
        }

        return null;
    }

    public SidecarProtocolFailure? ValidateRequest(SidecarRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion != SidecarProtocolContract.Version || string.IsNullOrWhiteSpace(request.RequestId) ||
            string.IsNullOrWhiteSpace(request.Method) || request.TimeoutMilliseconds is < 1 or > 300_000 ||
            request.AllowedRepositoryRelativePaths is null || request.AllowedRepositoryRelativePaths.Count == 0 ||
            request.AllowedRepositoryRelativePaths.Any(static path => !IsRepositoryRelativePath(path)) ||
            request.AllowedRepositoryRelativePaths.Distinct(StringComparer.Ordinal).Count() != request.AllowedRepositoryRelativePaths.Count ||
            !IsJson(request.PayloadJson))
        {
            return Failure("invalid-request", "Sidecar requests require a compatible version, bounded timeout, unique repository-relative paths, and JSON payload.");
        }

        return null;
    }

    public SidecarProtocolFailure? ValidateResponse(SidecarResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.ProtocolVersion != SidecarProtocolContract.Version || string.IsNullOrWhiteSpace(response.RequestId) ||
            (response.IsSuccess && (response.Error is not null || !IsJson(response.ResultJson))) ||
            (!response.IsSuccess && (response.Error is null || string.IsNullOrWhiteSpace(response.Error.Code) || string.IsNullOrWhiteSpace(response.Error.Message))))
        {
            return Failure("invalid-response", "Sidecar responses must contain either valid JSON success data or a complete failure object.");
        }

        return null;
    }

    private static bool AreCapabilitiesComplete(IReadOnlyList<SidecarCapability> capabilities) =>
        capabilities is not null && capabilities.All(static capability => capability is not null && !string.IsNullOrWhiteSpace(capability.Name) && capability.Version > 0) &&
        capabilities.Select(static capability => capability.Name).Distinct(StringComparer.Ordinal).Count() == capabilities.Count;

    private static bool IsToolVersion(string version) => Version.TryParse(version, out var parsed) && parsed.Major >= 0;

    private static bool IsRepositoryRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or "..");

    private static bool IsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { using var _ = JsonDocument.Parse(value); return true; } catch (JsonException) { return false; }
    }

    private static SidecarProtocolFailure Failure(string code, string message) => new(code, message);
}
