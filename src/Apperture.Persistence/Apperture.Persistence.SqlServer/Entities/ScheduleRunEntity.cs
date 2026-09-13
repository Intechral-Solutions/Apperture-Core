namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Per-firing schedule run record. Epic 28, Epic 36 (multi-host claim ownership).
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

    /// <summary>Epic 36: Host that claimed this firing. Null for pre-migration rows.</summary>
    public string? ClaimedByHostId { get; set; }

    /// <summary>
    /// CTX-2: immutable ResourceContext snapshot, copied from <c>ScheduleDefinition.ResourceContextId</c>
    /// at claim time (not at successful dispatch). Never rewritten afterward, including when the
    /// schedule's current ownership later changes. Null for pre-CTX-2 rows.
    /// </summary>
    public string? ResourceContextId { get; set; }
}
