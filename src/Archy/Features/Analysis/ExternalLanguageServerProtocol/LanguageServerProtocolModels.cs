namespace Archy.Features.Analysis.ExternalLanguageServerProtocol;

public sealed record LanguageServerLaunchSpecification(
    string SchemaVersion,
    string ServerId,
    string LanguageId,
    string Command,
    IReadOnlyList<string> Arguments,
    string RepositoryRoot,
    LanguageServerTimeouts Timeouts,
    LanguageServerRestartPolicy RestartPolicy,
    int MaximumMessageBytes,
    int MaximumStandardErrorBytes,
    IReadOnlyList<LanguageServerCapabilityRequirement> RequiredCapabilities);

public sealed record LanguageServerTimeouts(
    int InitializeMilliseconds,
    int RequestMilliseconds,
    int ShutdownMilliseconds);

public sealed record LanguageServerRestartPolicy(
    int MaximumRestarts,
    int InitialBackoffMilliseconds);

public sealed record LanguageServerCapabilityRequirement(string Name, bool RequiredForSemanticAnalysis);

public sealed record LanguageServerCapabilityAdvertisement(string Name, bool IsAvailable, string? Detail);

public sealed record LanguageServerCapabilityStatus(string Name, LanguageServerCapabilityState State, string? Detail);

public sealed record LanguageServerCapabilityProfile(
    LanguageServerVersionReport Version,
    IReadOnlyList<LanguageServerCapabilityStatus> Capabilities);

public sealed record LanguageServerVersionReport(string? Name, string? Version)
{
    public bool WasReported => !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(Version);
}

public sealed record LanguageServerRestartDecision(
    bool ShouldRestart,
    int NextRestartNumber,
    int DelayMilliseconds,
    string Reason);

public sealed record LspInitializeRequest(
    string JsonRpc,
    int Id,
    string Method,
    [property: System.Text.Json.Serialization.JsonPropertyName("params")] LspInitializeParameters Parameters);

public sealed record LspInitializeParameters(
    int? ProcessId,
    string RootUri,
    IReadOnlyList<LspWorkspaceFolder> WorkspaceFolders,
    LspInitializeClientCapabilities Capabilities,
    LspClientInfo ClientInfo,
    ArchyLspInitializationOptions InitializationOptions);

public sealed record LspWorkspaceFolder(string Uri, string Name);

public sealed record LspInitializeClientCapabilities(
    LspWorkspaceClientCapabilities Workspace,
    LspTextDocumentClientCapabilities TextDocument);

public sealed record LspWorkspaceClientCapabilities(bool WorkspaceFolders);

public sealed record LspTextDocumentClientCapabilities(
    LspRequestClientCapability DocumentSymbol,
    LspRequestClientCapability Definition,
    LspRequestClientCapability References,
    LspRequestClientCapability CallHierarchy,
    LspRequestClientCapability TypeDefinition);

public sealed record LspRequestClientCapability(bool DynamicRegistration);

public sealed record LspClientInfo(string Name, string Version);

public sealed record ArchyLspInitializationOptions(
    string ProtocolSchemaVersion,
    string SemanticContractSchemaVersion,
    bool RequiresSnapshotBoundResults);

public sealed record LspProtocolTranscriptAttempt(
    int AttemptNumber,
    IReadOnlyList<LspProtocolTranscriptEvent> Events);

public sealed record LspProtocolTranscriptEvent(
    LspProtocolTranscriptDirection Direction,
    LspProtocolTranscriptEventKind Kind,
    int? RequestId,
    string? Method);

public enum LanguageServerCapabilityState
{
    Available,
    Unavailable,
    Degraded,
}

public enum LanguageServerFailureKind
{
    InitializeTimeout,
    RequestTimeout,
    MalformedResponse,
    ProcessExited,
}

public enum LspProtocolTranscriptDirection
{
    Host,
    Server,
    Process,
}

public enum LspProtocolTranscriptEventKind
{
    Restarted,
    JsonRpcRequest,
    JsonRpcNotification,
    JsonRpcResponse,
    RequestTimedOut,
    MalformedResponse,
    ProcessExited,
}
