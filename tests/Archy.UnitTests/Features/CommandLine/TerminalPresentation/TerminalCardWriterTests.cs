using Archy.Features.CommandLine.TerminalPresentation;

namespace Archy.UnitTests.Features.CommandLine.TerminalPresentation;

public sealed class TerminalCardWriterTests
{
    [Fact]
    public void WriteWithoutColorProducesCopySafePlainText()
    {
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        TerminalCardWriter.Write(output, new TerminalCard(
            "ARCHY · MCP READY",
            "Architecture tools are available.",
            [new TerminalDetail("Transport", "stdio · protocol-safe")]), useColor: false);

        var rendered = output.ToString();
        Assert.Contains("ARCHY · MCP READY", rendered, StringComparison.Ordinal);
        Assert.Contains("stdio · protocol-safe", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteWithColorUsesTailwindBlue600()
    {
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        TerminalCardWriter.Write(output, new TerminalCard("Title", "Summary", []), useColor: true);

        var rendered = output.ToString();
        Assert.Contains("\u001b[38;2;37;99;235m", rendered, StringComparison.Ordinal);
        Assert.Contains("\u001b[38;2;255;255;255m", rendered, StringComparison.Ordinal);
        Assert.Contains("\u001b[38;2;156;163;175m", rendered, StringComparison.Ordinal);
    }
}
