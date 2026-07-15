using Archy.SharedKernel.Primitives;

namespace Archy.Features.Duplicates.ParseJscpdCloneReport;

public interface IJscpdCloneReportParser
{
    Result<JscpdCloneReport> Parse(string resultJson);
}
