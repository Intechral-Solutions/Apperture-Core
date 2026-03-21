namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Per-assignment computed schedule state. Epic 28.
/// </summary>
public sealed class ScheduleStateEntity
{
    public string AssignmentId { get; set; } = "";
    public DateTimeOffset? NextDueUtc { get; set; }
    public DateTimeOffset? LastAttemptedUtc { get; set; }
    public string? LastOutcome { get; set; }
    public string? LastOutcomeReason { get; set; }
    public DateTimeOffset? LastSuccessfulDispatchUtc { get; set; }
}
