namespace Archy.Features.Advisories.ComposeUnifiedAdvisories;
public sealed record AdvisoryItem(string Kind,double Confidence,string Message,IReadOnlyList<string> EvidenceIds,bool IsAbstention);
