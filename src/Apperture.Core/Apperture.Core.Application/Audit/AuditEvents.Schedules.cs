namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 3: the typed producers of the schedule hard-delete. The schedule is the target and is also kept as the related
// schedule reference; its owning resource context is the related context, as the previous records did. The name is a snapshot,
// since the schedule no longer exists once the row is written. The recurrence and the target configuration are never recorded.
public static partial class AuditEvents
{
    /// <summary>The bounded reasons a schedule delete is refused.</summary>
    public static class ScheduleDeleteRejections
    {
        /// <summary>The schedule is still enabled and must be disabled first.</summary>
        public const string Enabled = "enabled";
    }

    private const int AuditLogScheduleIdLength = 128;

    /// <summary>
    /// A disabled schedule, its assignments and those assignments' state cursors were removed. Written in the same transaction
    /// as the removal. <paramref name="assignmentsRemoved"/> is the number of assignments the delete removed.
    /// </summary>
    public static AuditEvent ScheduleDeleted(
        string scheduleId,
        string name,
        string? integrationId,
        int assignmentsRemoved,
        string? resourceContextId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignmentsRemoved);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ScheduleDeleted),
            Target(AuditTargetTypes.Schedule, scheduleId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("integrationId", integrationId), ("assignmentsRemoved", assignmentsRemoved)),
            ScheduleRelated(scheduleId, resourceContextId));
    }

    /// <summary>
    /// An authorized delete was refused because the schedule is enabled, and nothing changed. A W3 row written after the
    /// attempt's transaction ended; <paramref name="reason"/> is one of <see cref="ScheduleDeleteRejections"/>.
    /// </summary>
    public static AuditEvent ScheduleDeleteRejected(
        string scheduleId,
        string name,
        string? integrationId,
        string? resourceContextId,
        string reason = ScheduleDeleteRejections.Enabled) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ScheduleDeleteRejected),
            Target(AuditTargetTypes.Schedule, scheduleId),
            AuditOutcomeCodes.Rejected,
            Fields(("name", name), ("integrationId", integrationId), ("reason", reason)),
            ScheduleRelated(scheduleId, resourceContextId));

    /// <summary>
    /// A schedule was created, with the assignments created in the same transaction (the initial one, when the creator names a
    /// runtime plane). The recurrence, time zone, target configuration and execution policy reference are never recorded.
    /// </summary>
    public static AuditEvent ScheduleCreated(
        string scheduleId,
        string name,
        string? integrationId,
        bool enabled,
        string? runtimePlaneId,
        int assignmentsCreated,
        string? resourceContextId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(assignmentsCreated);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ScheduleCreated),
            Target(AuditTargetTypes.Schedule, scheduleId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("name", name),
                ("integrationId", integrationId),
                ("enabled", enabled),
                ("runtimePlaneId", BoundedId(runtimePlaneId)),
                ("assignmentsCreated", assignmentsCreated)),
            ScheduleRelated(scheduleId, resourceContextId));
    }

    /// <summary>
    /// A schedule's name, recurrence or time zone changed without a change of the enabled flag. <paramref name="changed"/> names
    /// the fields that differ (never their values) and is never empty.
    /// </summary>
    public static AuditEvent ScheduleUpdated(
        string scheduleId,
        string name,
        string? integrationId,
        IReadOnlyList<string> changed,
        string? resourceContextId)
    {
        var fields = ValidatedChanges(changed, AuditDetailSchemas.ScheduleChangeFields, "schedule", requireAny: true);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ScheduleUpdated),
            Target(AuditTargetTypes.Schedule, scheduleId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("integrationId", integrationId), ("changed", fields)),
            ScheduleRelated(scheduleId, resourceContextId));
    }

    /// <summary>
    /// A schedule was enabled or disabled. <paramref name="changed"/> names any other fields the same request changed (never
    /// their values) and may be empty.
    /// </summary>
    public static AuditEvent ScheduleEnabledChanged(
        bool enabled,
        string scheduleId,
        string name,
        string? integrationId,
        IReadOnlyList<string> changed,
        string? resourceContextId)
    {
        var fields = ValidatedChanges(changed, AuditDetailSchemas.ScheduleChangeFields, "schedule", requireAny: false);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(enabled ? AuditActionNames.ScheduleEnabled : AuditActionNames.ScheduleDisabled),
            Target(AuditTargetTypes.Schedule, scheduleId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("integrationId", integrationId), ("changed", fields.Count == 0 ? null : fields)),
            ScheduleRelated(scheduleId, resourceContextId));
    }

    /// <summary>
    /// An existing schedule was assigned to a runtime plane on its own. Whether policy overrides were supplied is recorded,
    /// never their content.
    /// </summary>
    public static AuditEvent ScheduleAssigned(string scheduleId, string runtimePlaneId, bool policyOverridesSet, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ScheduleAssigned),
            Target(AuditTargetTypes.Schedule, scheduleId),
            AuditOutcomeCodes.Succeeded,
            Fields(("runtimePlaneId", BoundedId(runtimePlaneId)), ("policyOverridesSet", policyOverridesSet)),
            ScheduleRelated(scheduleId, resourceContextId));

    private static AuditRelatedReferences ScheduleRelated(string scheduleId, string? resourceContextId) =>
        new(
            ScheduleId: BoundedId(scheduleId, AuditLogScheduleIdLength),
            ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength));

    private static Dictionary<string, object?> Fields(params (string Name, object? Value)[] fields) =>
        fields.Where(f => f.Value is not null).ToDictionary(f => f.Name, f => f.Value, StringComparer.Ordinal);
}
