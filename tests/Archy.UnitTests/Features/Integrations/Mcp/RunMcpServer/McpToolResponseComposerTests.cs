using System.Text.Json;
using Archy.Features.Integrations.Mcp.RunMcpServer;

namespace Archy.UnitTests.Features.Integrations.Mcp.RunMcpServer;

public sealed class McpToolResponseComposerTests
{
    [Fact]
    public void ComposeMirrorsTheStructuredPayloadIntoTheTextBlock()
    {
        const string toolResult = """
            {"content":[{"type":"text","text":"revision 112; edges 1"}],"structuredContent":{"revision":112,"edges":[{"source":"a","target":"b","kind":"calls","confidence":1}]}}
            """;

        var text = ReadText(McpToolResponseComposer.Compose(toolResult));

        Assert.StartsWith("revision 112; edges 1", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"calls\"", text, StringComparison.Ordinal);
        Assert.Contains("\"target\":\"b\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposePreservesTheStructuredContentFieldExactly()
    {
        const string toolResult = """
            {"content":[{"type":"text","text":"summary"}],"structuredContent":{"abstention":false,"candidates":[{"score":0.277083}]}}
            """;

        using var composed = JsonDocument.Parse(McpToolResponseComposer.Compose(toolResult));
        using var original = JsonDocument.Parse(toolResult);

        Assert.Equal(
            original.RootElement.GetProperty("structuredContent").GetRawText(),
            composed.RootElement.GetProperty("structuredContent").GetRawText());
    }

    [Fact]
    public void ComposeForwardsResultsThatCarryNoStructuredContent()
    {
        const string toolResult = """
            {"content":[{"type":"text","text":"nothing structured here"}]}
            """;

        Assert.Equal(toolResult, McpToolResponseComposer.Compose(toolResult));
    }

    [Fact]
    public void ComposeIsIdempotentSoAResultIsNeverMirroredTwice()
    {
        const string toolResult = """
            {"content":[{"type":"text","text":"revision 1; edges 0"}],"structuredContent":{"edges":[]}}
            """;

        var once = McpToolResponseComposer.Compose(toolResult);

        Assert.Equal(once, McpToolResponseComposer.Compose(once));
    }

    [Fact]
    public void ComposeStatesTruncationRatherThanSilentlyClippingALargePayload()
    {
        var edges = string.Join(',', Enumerable.Range(0, 4000).Select(index => $"{{\"source\":\"node-{index}\"}}"));
        var toolResult = $"{{\"content\":[{{\"type\":\"text\",\"text\":\"large\"}}],\"structuredContent\":{{\"edges\":[{edges}]}}}}";

        var composed = McpToolResponseComposer.Compose(toolResult);
        var text = ReadText(composed);

        Assert.Contains("[truncated after", text, StringComparison.Ordinal);
        Assert.Contains("structuredContent", text, StringComparison.Ordinal);

        // The mirrored text is bounded, but the machine-readable payload stays complete.
        using var document = JsonDocument.Parse(composed);
        Assert.Equal(4000, document.RootElement.GetProperty("structuredContent").GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public void ComposeForwardsPayloadsItCannotParseInsteadOfFailingTheCall()
    {
        const string malformed = "{\"content\":[";

        Assert.Equal(malformed, McpToolResponseComposer.Compose(malformed));
    }

    [Fact]
    public void ComposeMirrorsEvenWhenTheToolWroteNoTextBlock()
    {
        const string toolResult = """
            {"content":[],"structuredContent":{"revision":9}}
            """;

        Assert.Contains("\"revision\":9", ReadText(McpToolResponseComposer.Compose(toolResult)), StringComparison.Ordinal);
    }

    private static string ReadText(string composed)
    {
        using var document = JsonDocument.Parse(composed);
        return document.RootElement.GetProperty("content")[0].GetProperty("text").GetString()!;
    }
}
