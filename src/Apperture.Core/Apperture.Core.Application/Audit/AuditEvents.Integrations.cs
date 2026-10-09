namespace Apperture.Core.Application.Audit;

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
