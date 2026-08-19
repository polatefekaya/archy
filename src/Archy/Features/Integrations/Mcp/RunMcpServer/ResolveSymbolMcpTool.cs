using System.Text.Json;
using Archy.Features.Graph.CommitGraphRevision;
using Archy.Features.Graph.ExploreGraph;
using Archy.Features.Workspaces.AcquireWorkspaceLock;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>
/// Resolves a plain name, namespace fragment, or repository path into the stable identifiers that
/// the graph tools require.
/// </summary>
/// <remarks>
/// Every other graph tool is addressed by stable identifier, whose C# form is
/// <c>csharp:type:&lt;repository-relative-path&gt;:&lt;FullyQualified.TypeName&gt;</c>. A caller
/// holding only a type name cannot construct that string reliably, and a guessed identifier
/// produces a not-found failure rather than an answer. This tool closes that gap by returning
/// candidates read from the graph. It never fabricates an identifier: when nothing matches, it
/// abstains.
/// </remarks>
public sealed class ResolveSymbolMcpTool(IGraphExplorerReader? reader = null) : IMcpTool
{
    private const int DefaultLimit = 10;
    private const int MaximumLimit = 25;
    private const int MaximumQueryCharacters = 160;

    private readonly IGraphExplorerReader reader = reader ?? new GraphExplorerReader(new WorkspaceLockManager(TimeProvider.System));

    public string Name => "resolve_symbol";

    public string Description =>
        "Resolve a type, member, or file name into graph stable identifiers usable by the other Archy tools. Call this first when you hold a plain name rather than a stable identifier.";

    public string InputSchemaJson => """
        {"type":"object","properties":{"query":{"type":"string","minLength":1,"maxLength":160,"description":"Type name, namespace fragment, or repository-relative path fragment, for example: UnifiedAdvisoryComposer."},"limit":{"type":"integer","minimum":1,"maximum":25,"default":10,"description":"Maximum candidates to return."},"revision":{"type":"integer","minimum":1,"description":"Optional committed graph revision; the active revision is used when omitted."}},"required":["query"]}
        """;

    public async ValueTask<McpToolResult> ExecuteAsync(McpToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetProperty("query", out var rawQuery)
            || rawQuery.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(rawQuery.GetString()))
        {
            return McpToolResult.Failure("query is required.");
        }

        var query = rawQuery.GetString()!.Trim();
        if (query.Length > MaximumQueryCharacters)
        {
            return McpToolResult.Failure($"query must not exceed {MaximumQueryCharacters} characters.");
        }

        var limit = invocation.Arguments.TryGetProperty("limit", out var rawLimit) && rawLimit.TryGetInt32(out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, MaximumLimit)
            : DefaultLimit;
        long? revision = invocation.Arguments.TryGetProperty("revision", out var rawRevision) && rawRevision.TryGetInt64(out var parsedRevision)
            ? parsedRevision
            : null;

        var search = await this.reader.SearchAsync(invocation.Workspace.StateLocation, query, revision, cancellationToken);
        if (!search.IsSuccess)
        {
            return McpToolResult.Failure(search.Problem!.Message);
        }

        var matches = search.Value!;
        if (matches.Count == 0)
        {
            return McpToolResult.Success(
                $"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String($"No graph node matches \"{query}\". The symbol may be outside the configured scope, or the graph may predate it; run 'archy analyze' and retry before concluding it does not exist.")}}}],\"structuredContent\":{{\"abstention\":true,\"query\":{McpJson.String(query)},\"candidates\":[]}}}}");
        }

        var candidates = string.Join(',', matches.Take(limit).Select(Serialize));
        var shown = Math.Min(limit, matches.Count);
        var summary = shown < matches.Count
            ? $"Resolved {matches.Count} candidate(s) for \"{query}\"; returning the first {shown} by match quality."
            : $"Resolved {shown} candidate(s) for \"{query}\".";

        return McpToolResult.Success(
            $"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(summary)}}}],\"structuredContent\":{{\"abstention\":false,\"query\":{McpJson.String(query)},\"truncated\":{McpJson.Boolean(shown < matches.Count)},\"candidates\":[{candidates}]}}}}");
    }

    private static string Serialize(GraphNodeFact node) =>
        $"{{\"stableId\":{McpJson.String(node.StableId)}," +
        $"\"displayName\":{McpJson.String(node.DisplayName)}," +
        $"\"nodeKind\":{McpJson.String(node.NodeKind)}," +
        $"\"filePath\":{(node.FilePath is null ? "null" : McpJson.String(node.FilePath))}," +
        $"\"startLine\":{(node.StartLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")}," +
        $"\"provider\":{McpJson.String(node.Provider)}," +
        $"\"confidence\":{node.Confidence.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}}";
}
