namespace Archy.Features.CommandLine.TerminalPresentation;

/// <summary>Structured content for a human-facing terminal panel.</summary>
public sealed record TerminalCard(
    string Title,
    string Summary,
    IReadOnlyList<TerminalDetail> Details,
    string? Footer = null);

/// <summary>A label/value row within a <see cref="TerminalCard"/>.</summary>
public sealed record TerminalDetail(string Label, string Value);
