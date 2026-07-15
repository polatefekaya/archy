namespace Archy.Features.Memory.AuthorizeAiSourceSharing;

public sealed record AiSourceSharingDecision(bool IsAllowed, string Reason);
