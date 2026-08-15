namespace Archy.Features.Sessions.ArchitectureSessions;

public enum SessionEventKind
{
    SessionStarted,
    FileTouched,
    ValidationCompleted,
    PreflightContextRecorded,
    PreflightContextInvalidated,
    DecisionRecorded,
    SummaryBatchRequested,
    SessionEnded,
}
