using Archy.Features.CommandLine.TerminalPresentation;

namespace Archy.UnitTests.Features.CommandLine.TerminalPresentation;

public sealed class TerminalCardRendererTests
{
    [Fact]
    public void RenderBuildsABoundedStructuredCard()
    {
        var lines = TerminalCardRenderer.Render(new TerminalCard(
            "ARCHY · ANALYSIS",
            "A fresh graph revision is ready.",
            [new TerminalDetail("Graph revision", "42")],
            "Next: archy web serve"));

        Assert.StartsWith("╭", lines[0]);
        Assert.Contains(lines, static line => line.Contains("ARCHY · ANALYSIS", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.Contains("Graph revision  42", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.Contains("Next: archy web serve", StringComparison.Ordinal));
        Assert.StartsWith("╰", lines[^1]);
    }

    [Fact]
    public void RenderNormalizesLineBreaksAndTruncatesLongValues()
    {
        var value = $"first line{Environment.NewLine}{new string('x', 160)}";
        var lines = TerminalCardRenderer.Render(new TerminalCard(
            "Title",
            "Summary",
            [new TerminalDetail("Value", value)]));

        Assert.DoesNotContain(lines, static line => line.Contains(Environment.NewLine, StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.Contains('…'));
        Assert.All(lines, static line => Assert.True(line.Length <= 80));
    }
}
