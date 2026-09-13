namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Audit log entity. Epic 37: Outcome, ModuleId, ScheduleId, RunId are first-class columns.
/// </summary>
public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Details { get; set; }
    /// <summary>First-class outcome. Epic 37.</summary>
    public string? Outcome { get; set; }
    /// <summary>First-class module reference. Epic 37.</summary>
    public string? ModuleId { get; set; }
    /// <summary>First-class schedule reference. Epic 37.</summary>
    public string? ScheduleId { get; set; }
    /// <summary>First-class run reference. Epic 37.</summary>
    public string? RunId { get; set; }

    /// <summary>
    /// CTX-2: ResourceContext of the resource this audit entry concerns, when the calling action
    /// already has authoritative context available (e.g. from the Integration/Connection/Schedule/
    /// Run in hand). Never resolved by a second lookup inside generic audit append. Null for
    /// System/IAM/platform-settings audit and for pre-CTX-2 rows.
    /// </summary>
    public string? ResourceContextId { get; set; }
}
