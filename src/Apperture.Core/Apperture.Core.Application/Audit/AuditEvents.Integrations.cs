using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 4: the typed producers of the integration create, update and state transitions (below, before the delete).
// AUD-1.4 slice 3, work package 4a: the typed producer of the integration hard-delete. The integration is the target; its owning
// resource context and its module are the related references. The name is a snapshot, since the integration no longer exists
// once the row is written. No setting value, secret or secret reference has a field: the overrides the delete removed are only
// counted, and a secret it retired has its own Secrets-category row (see IntegrationSettingSecretRetired).
public static partial class AuditEvents
{
    private const int AuditLogModuleIdLength = 128;

    /// <summary>The retirement reason of a secret an integration delete retired with the integration setting that referenced it.</summary>
    public const string IntegrationDeletedSecretRetirementReason = "integration_deleted";

    /// <summary>
    /// An integration was created. Whether a settings dictionary was supplied is recorded, never its keys or values.
    /// </summary>
    public static AuditEvent IntegrationCreated(
        string integrationId,
        string name,
        string? moduleId,
        bool settingsProvided,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.IntegrationCreated),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("moduleId", BoundedId(moduleId)), ("settingsProvided", settingsProvided)),
            IntegrationRelated(moduleId, resourceContextId));

    /// <summary>
    /// An integration's display name or settings changed without a change of state. <paramref name="changed"/> names the fields
    /// that differ (never their values) and is never empty: an update that changes nothing is not recorded.
    /// </summary>
    public static AuditEvent IntegrationUpdated(
        string integrationId,
        string name,
        string? moduleId,
        IReadOnlyList<string> changed,
        string? resourceContextId)
    {
        var fields = ValidatedChanges(changed, AuditDetailSchemas.IntegrationChangeFields, "integration", requireAny: true);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.IntegrationUpdated),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("moduleId", BoundedId(moduleId)), ("changed", fields)),
            IntegrationRelated(moduleId, resourceContextId));
    }

    /// <summary>
    /// An integration's state changed. The action names the transition (<c>activated</c>, <c>deactivated</c>, <c>retired</c>);
    /// <paramref name="changed"/> names any other fields the same request changed (never their values) and may be empty.
    /// </summary>
    public static AuditEvent IntegrationStateChanged(
        string action,
        string integrationId,
        string name,
        string? moduleId,
        IntegrationState fromState,
        IntegrationState toState,
        IReadOnlyList<string> changed,
        string? resourceContextId)
    {
        if (action is not (AuditActionNames.IntegrationActivated or AuditActionNames.IntegrationDeactivated or AuditActionNames.IntegrationRetired))
            throw new ArgumentException($"'{action}' is not an integration state transition.", nameof(action));
        if (fromState == toState)
            throw new ArgumentException("A request that leaves the state as it was is not recorded as a transition.", nameof(toState));
        var expected = toState switch
        {
            IntegrationState.Active => AuditActionNames.IntegrationActivated,
            IntegrationState.Inactive => AuditActionNames.IntegrationDeactivated,
            _ => AuditActionNames.IntegrationRetired,
        };
        if (!string.Equals(action, expected, StringComparison.Ordinal))
            throw new ArgumentException($"'{action}' does not describe a change to {toState}.", nameof(action));
        var fields = ValidatedChanges(changed, AuditDetailSchemas.IntegrationChangeFields, "integration", requireAny: false);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("name", name),
                ("moduleId", BoundedId(moduleId)),
                ("fromState", StateName(fromState)),
                ("toState", StateName(toState)),
                ("changed", fields.Count == 0 ? null : fields)),
            IntegrationRelated(moduleId, resourceContextId));
    }

    private static string StateName(IntegrationState state) => state.ToString().ToLowerInvariant();

    private static AuditRelatedReferences IntegrationRelated(string? moduleId, string? resourceContextId) =>
        new(
            ModuleId: BoundedId(moduleId, AuditLogModuleIdLength),
            ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength));

    /// <summary>The changed-field names of an update: only declared ones, each once, in the declared order.</summary>
    private static List<string> ValidatedChanges(IReadOnlyList<string> changed, IReadOnlyList<string> allowed, string entity, bool requireAny)
    {
        if (requireAny && changed.Count == 0)
            throw new ArgumentException("An update that changes nothing is not audited as a change.", nameof(changed));
        if (changed.FirstOrDefault(c => !allowed.Contains(c, StringComparer.Ordinal)) is { } unknown)
            throw new ArgumentException($"'{unknown}' is not a changeable {entity} field.", nameof(changed));
        return allowed.Where(a => changed.Contains(a, StringComparer.Ordinal)).ToList();
    }

    /// <summary>
    /// An integration and its persisted integration-scoped setting overrides were removed. Written in the same transaction as the
    /// removal. <paramref name="settingsRemoved"/> is the number of overrides removed, whether or not a setting definition for
    /// each still exists.
    /// </summary>
    public static AuditEvent IntegrationDeleted(
        string integrationId,
        string name,
        string? moduleId,
        int settingsRemoved,
        string? resourceContextId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(settingsRemoved);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.IntegrationDeleted),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("moduleId", BoundedId(moduleId)), ("settingsRemoved", settingsRemoved)),
            new AuditRelatedReferences(
                ModuleId: BoundedId(moduleId, AuditLogModuleIdLength),
                ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength)));
    }
}
