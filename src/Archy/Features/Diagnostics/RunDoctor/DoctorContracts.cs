using Mediator;
using Archy.SharedKernel.Primitives;

namespace Archy.Features.Diagnostics.RunDoctor;

public enum DoctorSeverity { Info, Warning, Error }
public sealed record DoctorCheck(string Id, DoctorSeverity Severity, string Detail, string? Remediation);
public sealed record DoctorReport(IReadOnlyList<DoctorCheck> Checks, int ErrorCount, int WarningCount, int InfoCount)
{
    public bool HasErrors => ErrorCount > 0;
}
public sealed record RunDoctorQuery(string Path, string? ConfigurationPath, string? StateRoot) : IRequest<Result<DoctorReport>>;
