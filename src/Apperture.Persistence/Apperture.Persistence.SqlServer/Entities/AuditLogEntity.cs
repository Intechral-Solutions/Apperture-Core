namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Audit log entity. Epic 37: Outcome, ModuleId, ScheduleId, RunId are first-class columns.
/// </summary>
/// <remarks>
/// AUD-1.1 (audit hardening design gate §10.1, §11.8, §13, §14, §19): the event identity, producer and details format
/// are evidence and never change after insert. The classification set (<see cref="ClassificationState"/> through
/// <see cref="OutcomeProvenance"/>) is either inserted complete or completed by one conditional transition, then frozen
/// as a whole. The database refuses anything else (triggers and check constraints added by
/// <c>Aud1_AuditFoundation</c>).
/// </remarks>
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

    /// <summary>
    /// CTX-3B: links two related audit records (e.g. a move's departure and arrival rows) that share
    /// one operation. Null for every audit action that predates this column and for actions that are
    /// not part of a multi-record operation.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>Stable identity of this event. Unique when present; null only on rows written before AUD-1.1.</summary>
    public string? EventId { get; set; }

    /// <summary>The <see cref="EventId"/> of the primary row of the operation that wrote this row.</summary>
    public string? OperationEventId { get; set; }

    /// <summary><c>recorder</c>, <c>append</c>, or null for rows written before AUD-1.1.</summary>
    public string? Producer { get; set; }

    /// <summary><c>json/1</c> for recorder rows; null for append and legacy rows.</summary>
    public string? DetailsFormat { get; set; }

    /// <summary>Null (unclassified) or <c>complete</c>.</summary>
    public string? ClassificationState { get; set; }

    public string? ActorKind { get; set; }
    public string? ActorUserId { get; set; }

    /// <summary>The actor account's username as read by the recording transaction, or a recorded sign-in name.</summary>
    public string? ActorUsername { get; set; }

    public string? ActorExternalProvider { get; set; }
    public string? ActorExternalTenantId { get; set; }
    public string? ActorExternalSubjectId { get; set; }
    public string? ActorAttribution { get; set; }
    public string? AttributionIssue { get; set; }
    public string? AttributionIssueCertainty { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public string? Visibility { get; set; }
    public string? Basis { get; set; }
    public string? OutcomeCode { get; set; }
    public string? OutcomeProvenance { get; set; }
}
