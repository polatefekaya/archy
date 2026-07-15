namespace Archy.Features.Sessions.ArchitectureSessions;

public enum SessionEventKind
{
    SessionStarted,
    FileTouched,
    ValidationCompleted,
    DecisionRecorded,
    SummaryBatchRequested,
    SessionEnded,
}
