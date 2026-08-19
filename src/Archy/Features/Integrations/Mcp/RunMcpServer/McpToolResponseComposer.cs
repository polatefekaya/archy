using System.Text.Json;

namespace Archy.Features.Integrations.Mcp.RunMcpServer;

/// <summary>
/// Mirrors a tool result's <c>structuredContent</c> payload into its <c>content</c> text block.
/// </summary>
/// <remarks>
/// <para>
/// MCP servers that return <c>structuredContent</c> are expected to also return the serialized
/// payload in a text block, because a client is only required to surface <c>content</c>. Archy's
/// tools write a short human summary into <c>content</c> and the answer into
/// <c>structuredContent</c>; without mirroring, a client that reads only <c>content</c> sees a
/// count such as <c>"edges 3"</c> and never learns which three.
/// </para>
/// <para>
/// This runs at the protocol boundary rather than inside each tool so that every current and
/// future tool is covered by construction. Composition never fails a response: any payload this
/// component cannot parse is forwarded unchanged.
/// </para>
/// </remarks>
internal static class McpToolResponseComposer
{
    /// <summary>Maximum serialized payload characters mirrored into the text block.</summary>
    internal const int MaximumMirroredCharacters = 24_000;

    /// <summary>Separates the human summary from the mirrored payload.</summary>
    private const string PayloadHeading = "\n\nstructuredContent:\n";

    public static string Compose(string toolResultJson)
    {
        if (string.IsNullOrWhiteSpace(toolResultJson))
        {
            return toolResultJson;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(toolResultJson);
        }
        catch (JsonException)
        {
            return toolResultJson;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("structuredContent", out var structured)
                || structured.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return toolResultJson;
            }

            var payload = structured.GetRawText();
            var summary = ReadSummary(root);
            if (summary.Contains(PayloadHeading, StringComparison.Ordinal))
            {
                return toolResultJson;
            }

            return $"{{\"content\":[{{\"type\":\"text\",\"text\":{McpJson.String(summary + PayloadHeading + Fit(payload))}}}],\"structuredContent\":{payload}}}";
        }
    }

    /// <summary>
    /// Returns the payload unchanged when it fits, otherwise a prefix followed by an explicit
    /// notice. Truncation is always stated: a caller must never mistake a clipped payload for a
    /// complete one.
    /// </summary>
    private static string Fit(string payload)
    {
        if (payload.Length <= MaximumMirroredCharacters)
        {
            return payload;
        }

        return string.Concat(
            payload.AsSpan(0, MaximumMirroredCharacters),
            $"\n\n[truncated after {MaximumMirroredCharacters} of {payload.Length} characters; read the complete payload from this result's structuredContent field]");
    }

    private static string ReadSummary(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "text", StringComparison.Ordinal)
                && block.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
