namespace Archy.Features.Sidecars.Protocol;

public sealed record SidecarHandshakeRequest(
    int ProtocolVersion,
    string HostVersion,
    IReadOnlyList<SidecarCapability> RequiredCapabilities);

public sealed record SidecarHandshakeResponse(
    int ProtocolVersion,
    string SidecarName,
    string ToolVersion,
    IReadOnlyList<SidecarCapability> Capabilities);
