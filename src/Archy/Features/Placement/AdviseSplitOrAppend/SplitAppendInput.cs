namespace Archy.Features.Placement.AdviseSplitOrAppend;

public sealed record SplitAppendInput(string ModuleKey, int MemberCount, double Cohesion, double FanIn, double FanOut, string? SuggestedFileName);
