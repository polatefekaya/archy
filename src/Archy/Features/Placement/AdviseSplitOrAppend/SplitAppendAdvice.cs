namespace Archy.Features.Placement.AdviseSplitOrAppend;

public enum SplitAppendAdviceKind { Append = 1, CreateNewFile = 2, Abstain = 3 }
public sealed record SplitAppendAdvice(SplitAppendAdviceKind Kind, string Reason, string? SuggestedFileName);
