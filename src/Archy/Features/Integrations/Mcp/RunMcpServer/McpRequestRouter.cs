using System.Text.Json;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>Protocol router shared by every transport. It has no console or socket dependency.</summary>
public sealed class McpRequestRouter(McpToolCatalog tools)
{
    public int ToolCount => tools.Count;

    public async ValueTask<string?> RouteAsync(
        string requestJson,
        McpWorkspaceContext? workspace,
        CancellationToken cancellationToken)
    {
        if (requestJson.Length > McpTransportLimits.MaximumRequestCharacters)
        {
            return Error(null, -32600, "Request exceeds the MCP message limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(requestJson);
            return await RouteAsync(document.RootElement, workspace, cancellationToken);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error.");
        }
    }

    private async ValueTask<string?> RouteAsync(
        JsonElement request,
        McpWorkspaceContext? workspace,
        CancellationToken cancellationToken)
    {
        var id = request.ValueKind == JsonValueKind.Object && request.TryGetProperty("id", out var rawId)
            ? rawId.GetRawText()
            : null;
        if (request.ValueKind != JsonValueKind.Object
            || !request.TryGetProperty("jsonrpc", out var version)
            || version.GetString() != "2.0"
            || !request.TryGetProperty("method", out var method)
            || method.ValueKind != JsonValueKind.String)
        {
            return Error(id, -32600, "Invalid JSON-RPC request.");
        }

        if (method.GetString() == "notifications/initialized")
        {
            return null;
        }

        if (workspace is null)
        {
            return Error(id, -32002, "Archy could not initialize a repository workspace.");
        }

        if (method.GetString() == "initialize")
        {
            return Result(id, "{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{\"tools\":{}},\"instructions\":\"Use Archy tools for preflight and post-edit guidance. Deterministic violations are reported separately; Archy cannot block arbitrary writes.\",\"serverInfo\":{\"name\":\"archy\",\"version\":\"0.1.0-dev\"}}");
        }

        if (method.GetString() == "tools/list")
        {
            return Result(id, tools.ListResultJson);
        }

        if (method.GetString() != "tools/call"
            || !request.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("name", out var toolName)
            || !parameters.TryGetProperty("arguments", out var arguments)
            || !tools.TryGet(toolName.GetString() ?? string.Empty, out var tool))
        {
            return Error(id, -32602, "Invalid tool call.");
        }

        var result = await tool!.ExecuteAsync(new McpToolInvocation(tool.Name, arguments, workspace), cancellationToken);
        return result.IsSuccess
            ? Result(id, result.ResultJson)
            : Error(id, -32003, result.ErrorMessage!);
    }

    private static string Result(string? id, string resultJson) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id ?? "null"},\"result\":{resultJson}}}";

    private static string Error(string? id, int code, string message) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id ?? "null"},\"error\":{{\"code\":{code},\"message\":{McpJson.String(message)}}}}}";
}
