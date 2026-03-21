namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Per-firing schedule run record. Epic 28.
/// </summary>
public sealed class ScheduleRunEntity
{
    public long Id { get; set; }
    public string ScheduleId { get; set; } = "";
    public string AssignmentId { get; set; } = "";
    public DateTimeOffset FireTimeUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Outcome { get; set; } = "";
    public string? OutcomeReason { get; set; }
    public string? RunId { get; set; }
}
