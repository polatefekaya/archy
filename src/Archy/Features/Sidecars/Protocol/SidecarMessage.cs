namespace Archy.Features.Sidecars.Protocol;

public sealed record SidecarRequestMessage(
    int ProtocolVersion,
    string RequestId,
    string Method,
    int TimeoutMilliseconds,
    IReadOnlyList<string> AllowedRepositoryRelativePaths,
    string PayloadJson);

public sealed record SidecarResponseMessage(
    int ProtocolVersion,
    string RequestId,
    bool IsSuccess,
    string? ResultJson,
    SidecarError? Error);

public sealed record SidecarError(string Code, string Message, bool IsRetryable);
