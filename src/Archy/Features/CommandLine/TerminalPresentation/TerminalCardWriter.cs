namespace Archy.Features.CommandLine.TerminalPresentation;

/// <summary>Writes cards with a Tailwind blue-600, white, and neutral-gray terminal theme.</summary>
public static class TerminalCardWriter
{
    private const string Reset = "\u001b[0m";
    private const string Blue600 = "\u001b[38;2;37;99;235m";
    private const string White = "\u001b[38;2;255;255;255m";
    private const string Gray = "\u001b[38;2;156;163;175m";
    private const string Bold = "\u001b[1m";

    public static void WriteToStandardOutput(TerminalCard card) =>
        Write(Console.Out, card, !Console.IsOutputRedirected && SupportsColor());

    public static void WriteToStandardError(TerminalCard card) =>
        Write(Console.Error, card, !Console.IsErrorRedirected && SupportsColor());

    public static void Write(TextWriter writer, TerminalCard card, bool useColor)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var lines = TerminalCardRenderer.Render(card);
        for (var index = 0; index < lines.Count; index++)
        {
            writer.WriteLine(useColor ? Colorize(lines[index], index, lines.Count, card.Footer is not null) : lines[index]);
        }
    }

    private static bool SupportsColor() =>
        !string.Equals(Environment.GetEnvironmentVariable("NO_COLOR"), "1", StringComparison.Ordinal)
        && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

    private static string Colorize(string line, int index, int lineCount, bool hasFooter)
    {
        if (line.StartsWith('╭') || line.StartsWith('├') || line.StartsWith('╰'))
        {
            return $"{Blue600}{line}{Reset}";
        }

        if (!line.StartsWith('│'))
        {
            return line;
        }

        var content = line[2..^2].TrimEnd();
        if (index == 1)
        {
            return $"{Blue600}│{Reset} {Bold}{White}{content}{Reset}{Blue600} │{Reset}";
        }

        if (index == 2 || hasFooter && index == lineCount - 2)
        {
            return $"{Blue600}│{Reset} {Gray}{content}{Reset}{Blue600} │{Reset}";
        }

        var split = content.IndexOf("  ", StringComparison.Ordinal);
        if (split < 0)
        {
            return $"{Blue600}│{Reset} {White}{content}{Reset}{Blue600} │{Reset}";
        }

        var label = content[..split];
        var value = content[(split + 2)..];
        return $"{Blue600}│{Reset} {Gray}{label}{Reset}  {White}{value}{Reset}{Blue600} │{Reset}";
    }
}
