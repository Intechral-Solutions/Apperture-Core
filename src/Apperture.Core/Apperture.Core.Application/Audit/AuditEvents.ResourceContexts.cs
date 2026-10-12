namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 5: the typed producers of the resource context lifecycle and of the ownership moves.
//
// A context is its own target and its own related context. A move is about the moved resource (the target) and two contexts: the
// moved_out row identifies the operation and has the source as its related context, the moved_in row is its secondary row and has
// the target, so a filter by either context finds the row about it and an unfiltered view shows both. The pair shares one
// correlation id (the operation event id). A rejected move is a separate attempt row of the same principal with the source as its
// related context. Names are snapshots; no row carries anything the moved resource holds.
public static partial class AuditEvents
{
    /// <summary>An Application context was created.</summary>
    public static AuditEvent ResourceContextCreated(string contextId, string name, string slug) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ResourceContextCreated),
            Target(AuditTargetTypes.ResourceContext, contextId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("slug", slug)),
            ContextSelf(contextId));

    /// <summary>
    /// A context's name, slug or description changed. <paramref name="changed"/> names the fields that differ (never their
    /// values) and is never empty: an update that changes nothing is not recorded.
    /// </summary>
    public static AuditEvent ResourceContextUpdated(string contextId, string name, IReadOnlyList<string> changed)
    {
        var fields = ValidatedChanges(changed, AuditDetailSchemas.ResourceContextChangeFields, "resource context", requireAny: true);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ResourceContextUpdated),
            Target(AuditTargetTypes.ResourceContext, contextId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("changed", fields)),
            ContextSelf(contextId));
    }

    /// <summary>An Application context was archived: nothing live was left in it.</summary>
    public static AuditEvent ResourceContextArchived(string contextId, string name) =>
        ContextStateEvent(AuditActionNames.ResourceContextArchived, contextId, name);

    /// <summary>An archived Application context was made active again.</summary>
    public static AuditEvent ResourceContextUnarchived(string contextId, string name) =>
        ContextStateEvent(AuditActionNames.ResourceContextUnarchived, contextId, name);

    private static AuditEvent ContextStateEvent(string action, string contextId, string name) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(AuditTargetTypes.ResourceContext, contextId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name)),
            ContextSelf(contextId));

    private static AuditRelatedReferences ContextSelf(string contextId) =>
        new(ResourceContextId: BoundedId(contextId, AuditLogResourceContextIdLength));

    /// <summary>
    /// The primary row of a committed ownership move: the resource left <paramref name="sourceContextId"/>. An integration move
    /// also moves its attached schedules, which <paramref name="movedScheduleIds"/> lists (null for a connection or a schedule).
    /// </summary>
    public static AuditEvent OwnershipMovedOut(
        string targetType,
        string resourceId,
        string name,
        string sourceContextId,
        string targetContextId,
        IReadOnlyList<string>? movedScheduleIds,
        string correlationId) =>
        OwnershipMoveEvent(MovedOutAction(targetType), targetType, resourceId, name, sourceContextId, targetContextId, movedScheduleIds, sourceContextId, correlationId);

    /// <summary>The secondary row of a committed ownership move: the resource arrived in <paramref name="targetContextId"/>.</summary>
    public static AuditEvent OwnershipMovedIn(
        string targetType,
        string resourceId,
        string name,
        string sourceContextId,
        string targetContextId,
        IReadOnlyList<string>? movedScheduleIds,
        string correlationId) =>
        OwnershipMoveEvent(MovedInAction(targetType), targetType, resourceId, name, sourceContextId, targetContextId, movedScheduleIds, targetContextId, correlationId);

    /// <summary>A move the platform refused. <paramref name="blockerCodes"/> are the closed blocker codes of the preview.</summary>
    public static AuditEvent OwnershipMoveRejected(
        string targetType,
        string resourceId,
        string name,
        string sourceContextId,
        string targetContextId,
        IReadOnlyList<string> blockerCodes)
    {
        if (blockerCodes.Count == 0)
            throw new ArgumentException("A rejected move names at least one blocker.", nameof(blockerCodes));
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(MoveRejectedAction(targetType)),
            Target(targetType, resourceId),
            AuditOutcomeCodes.Rejected,
            Fields(
                ("name", name),
                ("sourceContextId", BoundedId(sourceContextId)),
                ("targetContextId", BoundedId(targetContextId)),
                ("blockers", blockerCodes.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList())),
            new AuditRelatedReferences(ResourceContextId: BoundedId(sourceContextId, AuditLogResourceContextIdLength)));
    }

    private static AuditEvent OwnershipMoveEvent(
        string action,
        string targetType,
        string resourceId,
        string name,
        string sourceContextId,
        string targetContextId,
        IReadOnlyList<string>? movedScheduleIds,
        string relatedContextId,
        string correlationId)
    {
        List<string>? listed = null;
        int? count = null;
        if (movedScheduleIds is not null)
        {
            count = movedScheduleIds.Count;
            listed = movedScheduleIds.OrderBy(i => i, StringComparer.Ordinal).Take(AuditDetailSchemas.MaxMovedScheduleIds).ToList();
        }

        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(targetType, resourceId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("name", name),
                ("sourceContextId", BoundedId(sourceContextId)),
                ("targetContextId", BoundedId(targetContextId)),
                ("schedulesMoved", count),
                ("scheduleIds", listed is { Count: > 0 } ? listed : null)),
            new AuditRelatedReferences(
                ResourceContextId: BoundedId(relatedContextId, AuditLogResourceContextIdLength),
                CorrelationId: correlationId));
    }

    private static string MovedOutAction(string targetType) => targetType switch
    {
        AuditTargetTypes.Integration => AuditActionNames.IntegrationMovedOut,
        AuditTargetTypes.Connection => AuditActionNames.ConnectionMovedOut,
        AuditTargetTypes.Schedule => AuditActionNames.ScheduleMovedOut,
        _ => throw new ArgumentException($"'{targetType}' is not a movable resource.", nameof(targetType)),
    };

    private static string MovedInAction(string targetType) => targetType switch
    {
        AuditTargetTypes.Integration => AuditActionNames.IntegrationMovedIn,
        AuditTargetTypes.Connection => AuditActionNames.ConnectionMovedIn,
        AuditTargetTypes.Schedule => AuditActionNames.ScheduleMovedIn,
        _ => throw new ArgumentException($"'{targetType}' is not a movable resource.", nameof(targetType)),
    };

    private static string MoveRejectedAction(string targetType) => targetType switch
    {
        AuditTargetTypes.Integration => AuditActionNames.IntegrationMoveRejected,
        AuditTargetTypes.Connection => AuditActionNames.ConnectionMoveRejected,
        AuditTargetTypes.Schedule => AuditActionNames.ScheduleMoveRejected,
        _ => throw new ArgumentException($"'{targetType}' is not a movable resource.", nameof(targetType)),
    };
}
