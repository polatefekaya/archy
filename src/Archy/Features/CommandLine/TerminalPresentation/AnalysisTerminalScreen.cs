using Archy.Features.Analysis.AnalyzeWorkspace;

namespace Archy.Features.CommandLine.TerminalPresentation;

public static class AnalysisTerminalScreen
{
    public static void WriteStarting(string path) =>
        TerminalCardWriter.WriteToStandardOutput(new TerminalCard(
            "ARCHY  ·  ANALYSIS",
            "Discovering source structure and refreshing architectural memory.",
            [
                new TerminalDetail("Workspace", Path.GetFileName(Path.GetFullPath(path))),
                new TerminalDetail("Pipeline", "discover → parse → resolve → persist"),
            ],
            "Large repositories may take a moment on the first graph revision."));

    public static void WriteCompleted(WorkspaceAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var revision = analysis.GraphRevision?.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unchanged";
        TerminalCardWriter.WriteToStandardOutput(new TerminalCard(
            "ARCHY  ·  ANALYSIS COMPLETE",
            analysis.WasNoOp ? "Architecture memory is already current." : "A fresh architecture revision is ready.",
            [
                new TerminalDetail("Graph revision", revision),
                new TerminalDetail("C# diagnostics", (analysis.CSharpSyntaxFacts?.Diagnostics.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new TerminalDetail("DI edges", ((analysis.DependencyRegistrationFacts?.Edges.Count ?? 0) + (analysis.DependencyConsumptionFacts?.Edges.Count ?? 0)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new TerminalDetail("Configuration edges", ((analysis.ConfigurationReadFacts?.Edges.Count ?? 0) + (analysis.ConfigurationDefinitionFacts?.Edges.Count ?? 0)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new TerminalDetail("Status", analysis.IsComplete ? "complete" : "completed with diagnostics"),
            ],
            "Next: archy web serve  ·  archy verify"));
    }
}
