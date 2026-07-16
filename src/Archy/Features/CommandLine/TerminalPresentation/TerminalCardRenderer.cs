using System.Text;

namespace Archy.Features.CommandLine.TerminalPresentation;

/// <summary>Renders bounded, copy-safe terminal cards without terminal escape sequences.</summary>
public static class TerminalCardRenderer
{
    private const int MaximumContentWidth = 76;

    public static IReadOnlyList<string> Render(TerminalCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var rows = new List<string>
        {
            Normalize(card.Title),
            Normalize(card.Summary),
        };
        rows.AddRange(card.Details.Select(static detail => $"{Normalize(detail.Label)}  {Normalize(detail.Value)}"));
        if (!string.IsNullOrWhiteSpace(card.Footer))
        {
            rows.Add(Normalize(card.Footer));
        }

        var width = Math.Min(MaximumContentWidth, Math.Max(30, rows.Max(static row => row.Length)));
        var border = new string('─', width + 2);
        var output = new List<string>(rows.Count + 4)
        {
            $"╭{border}╮",
            Row(rows[0], width),
            Row(rows[1], width),
        };

        foreach (var detail in card.Details)
        {
            output.Add(Row($"{Normalize(detail.Label)}  {Normalize(detail.Value)}", width));
        }

        if (!string.IsNullOrWhiteSpace(card.Footer))
        {
            output.Add($"├{border}┤");
            output.Add(Row(Normalize(card.Footer), width));
        }

        output.Add($"╰{border}╯");
        return output;
    }

    private static string Row(string value, int width) => $"│ {Truncate(value, width).PadRight(width)} │";

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value)
        ? "—"
        : value.ReplaceLineEndings(" ").Trim();

    private static string Truncate(string value, int width) => value.Length <= width
        ? value
        : string.Create(width, (value, width), static (buffer, state) =>
        {
            state.value.AsSpan(0, state.width - 1).CopyTo(buffer);
            buffer[^1] = '…';
        });
}
