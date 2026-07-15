using System.Text.Json.Serialization;

namespace Archy.Features.Architecture.ExportSarif;

/// <summary>The minimal typed SARIF 2.1.0 wire contract Archy emits for architecture verification.</summary>
internal sealed record ArchitectureSarifLog(
    [property: JsonPropertyName("$schema")] string Schema,
    string Version,
    ArchitectureSarifRun[] Runs);

internal sealed record ArchitectureSarifRun(
    ArchitectureSarifTool Tool,
    ArchitectureSarifResult[] Results,
    ArchitectureSarifInvocation[] Invocations);

internal sealed record ArchitectureSarifTool(ArchitectureSarifDriver Driver);

internal sealed record ArchitectureSarifDriver(
    string Name,
    string InformationUri,
    ArchitectureSarifRule[] Rules);

internal sealed record ArchitectureSarifRule(string Id, ArchitectureSarifMessage ShortDescription);

internal sealed record ArchitectureSarifResult(
    string RuleId,
    string Level,
    ArchitectureSarifMessage Message,
    string BaselineState,
    ArchitectureSarifLocation[]? Locations,
    ArchitectureSarifResultProperties Properties);

internal sealed record ArchitectureSarifResultProperties(
    string FindingKey,
    string FindingStatus,
    long GraphRevision,
    string? ExceptionId,
    string? ExceptionState);

internal sealed record ArchitectureSarifLocation(ArchitectureSarifPhysicalLocation PhysicalLocation);

internal sealed record ArchitectureSarifPhysicalLocation(
    ArchitectureSarifArtifactLocation ArtifactLocation,
    ArchitectureSarifRegion? Region);

internal sealed record ArchitectureSarifArtifactLocation(string Uri);

internal sealed record ArchitectureSarifRegion(int StartLine, int? EndLine);

internal sealed record ArchitectureSarifInvocation(
    bool ExecutionSuccessful,
    ArchitectureSarifNotification[] ToolExecutionNotifications);

internal sealed record ArchitectureSarifNotification(
    string Level,
    ArchitectureSarifMessage Message,
    ArchitectureSarifNotificationProperties Properties);

internal sealed record ArchitectureSarifNotificationProperties(string ProblemCode);

internal sealed record ArchitectureSarifMessage(string Text);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ArchitectureSarifLog))]
internal sealed partial class ArchitectureSarifJsonContext : JsonSerializerContext;
