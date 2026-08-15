using Archy.Features.Diagnostics.RunDoctor;
using Mediator;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Exposes the existing non-mutating doctor report to MCP clients without duplicating readiness logic.</summary>
public sealed class GetDoctorReadinessMcpTool(IMediator mediator) : IMcpTool
{
    public string Name => "get_doctor_readiness";
    public string Description => "Read the non-mutating Archy readiness report for this repository, including workspace, graph, language, hook, and embedding checks.";
    public string InputSchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        var report = await mediator.Send(new RunDoctorQuery(invocation.Workspace.RepositoryRoot, null, null), cancellationToken);
        if (!report.IsSuccess) return McpToolResult.Failure(report.Problem!.Message);
        var checks = string.Join(',', report.Value!.Checks.Select(CheckJson));
        var text = McpJson.String($"Doctor readiness: {report.Value.ErrorCount} error(s), {report.Value.WarningCount} warning(s), {report.Value.InfoCount} info check(s).");
        return McpToolResult.Success($"{{\"content\":[{{\"type\":\"text\",\"text\":{text}}}],\"structuredContent\":{{\"hasErrors\":{McpJson.Boolean(report.Value.HasErrors)},\"errorCount\":{report.Value.ErrorCount},\"warningCount\":{report.Value.WarningCount},\"infoCount\":{report.Value.InfoCount},\"checks\":[{checks}],\"mutatedWorkingTree\":false}}}}");
    }

    private static string CheckJson(DoctorCheck check) =>
        $"{{\"id\":{McpJson.String(check.Id)},\"severity\":{McpJson.String(check.Severity.ToString())},\"detail\":{McpJson.String(check.Detail)},\"remediation\":{(check.Remediation is null ? "null" : McpJson.String(check.Remediation))}}}";
}
