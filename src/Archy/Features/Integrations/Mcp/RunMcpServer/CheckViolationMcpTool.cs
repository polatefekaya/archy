using Archy.Features.Architecture.VerificationBaselines;
using Archy.Features.Architecture.VerifyArchitecture;
using Mediator;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

public sealed class CheckViolationMcpTool(IMediator mediator) : IMcpTool
{
    public string Name => "check_violation";
    public string Description => "Run deterministic preflight architecture checks; it does not block writes.";
    public string InputSchemaJson => """
        {"type":"object","properties":{},"additionalProperties":false}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var verification = await mediator.Send(new VerifyArchitectureCommand(invocation.Workspace.RepositoryRoot, null, null), cancellationToken);
        if (!verification.IsSuccess) return McpToolResult.Failure(verification.Problem!.Message);
        var result = verification.Value!;
        var introduced = result.Baseline.Findings.Count(static finding => finding.Status == ArchitectureFindingStatus.Introduced);
        var legacy = result.Baseline.Findings.Count(static finding => finding.Status == ArchitectureFindingStatus.Legacy);
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":\"Introduced: {introduced}; legacy: {legacy}. This is advisory preflight guidance and cannot block writes.\"}}],\"structuredContent\":{{\"graphRevision\":{result.GraphRevision},\"introducedCount\":{introduced},\"legacyCount\":{legacy},\"blocksWrite\":false}}}}");
    }
}
