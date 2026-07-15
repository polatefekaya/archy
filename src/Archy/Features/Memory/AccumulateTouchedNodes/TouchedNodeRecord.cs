using Archy.Features.Memory.DetermineImportantNodes;

namespace Archy.Features.Memory.AccumulateTouchedNodes;

public sealed record TouchedNodeRecord(
    string SessionId,
    ImportantNodeEligibility Eligibility,
    IReadOnlyList<string> ChangedPaths);
