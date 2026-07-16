using Archy.Features.Architecture.VerifyArchitecture;

namespace Archy.Features.CommandLine.TerminalPresentation;

public static class VerificationTerminalScreen
{
    public static void Write(ArchitectureVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        var introduced = verification.Baseline.Findings.Count(static finding => finding.Status.ToString().Equals("Introduced", StringComparison.Ordinal));
        TerminalCardWriter.WriteToStandardOutput(new TerminalCard(
            verification.IsCompliant ? "ARCHY  ·  ARCHITECTURE VERIFIED" : "ARCHY  ·  ARCHITECTURE NEEDS ATTENTION",
            verification.IsCompliant ? "No introduced deterministic findings." : "Introduced findings require review before delivery.",
            [
                new TerminalDetail("Graph revision", verification.GraphRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new TerminalDetail("Baseline", verification.Baseline.Status.ToString().ToLowerInvariant()),
                new TerminalDetail("Introduced findings", introduced.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new TerminalDetail("Active exceptions", verification.Exceptions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            verification.IsCompliant ? "Architecture gate passed." : "Inspect findings, accept only narrow justified exceptions, then retry."));
    }
}
