using Apperture.Core.Application.Integration;

namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 7: the typed producers of the attention routing settings. The instance default targets the instance; an override
// targets its integration, with the integration's name as a snapshot and its owning resource context as the related reference. A
// row names the condition the routing is for and, for an update, the fields that changed. Overrides accept condition keys that
// nothing validates, so any key other than the one the platform defines is recorded as "other": a caller's text never reaches a
// field. Team ids, durations, flag values, recipients and addresses are never recorded.
public static partial class AuditEvents
{
    private const string OtherConditionKey = "other";

    private static string ConditionKeyValue(string? conditionKey) =>
        string.Equals(conditionKey, AttentionConditionKeys.ExpectedRunMissing, StringComparison.Ordinal)
            ? AttentionConditionKeys.ExpectedRunMissing
            : OtherConditionKey;

    private static AuditRelatedReferences AttentionRoutingRelated(string? resourceContextId) =>
        new(ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength));

    /// <summary>The instance-wide default routing changed. <paramref name="changedFields"/> are drawn from the routing field names.</summary>
    public static AuditEvent AttentionRoutingDefaultUpdated(string conditionKey, IReadOnlyList<string> changedFields)
    {
        var changed = ValidatedChanges(changedFields, AuditDetailSchemas.AttentionRoutingChangeFields, "attention routing", requireAny: true);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.AttentionRoutingDefaultUpdated),
            Target(AuditTargetTypes.Instance, InstanceTargetId),
            AuditOutcomeCodes.Succeeded,
            Fields(("conditionKey", ConditionKeyValue(conditionKey)), ("changedFields", changed)));
    }

    /// <summary>An integration gained a routing override.</summary>
    public static AuditEvent AttentionRoutingOverrideCreated(string integrationId, string name, string conditionKey, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.AttentionRoutingOverrideCreated),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("conditionKey", ConditionKeyValue(conditionKey))),
            AttentionRoutingRelated(resourceContextId));

    /// <summary>An integration's routing override changed. <paramref name="changedFields"/> are drawn from the routing field names.</summary>
    public static AuditEvent AttentionRoutingOverrideUpdated(
        string integrationId, string name, string conditionKey, IReadOnlyList<string> changedFields, string? resourceContextId)
    {
        var changed = ValidatedChanges(changedFields, AuditDetailSchemas.AttentionRoutingChangeFields, "attention routing", requireAny: true);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.AttentionRoutingOverrideUpdated),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("conditionKey", ConditionKeyValue(conditionKey)), ("changedFields", changed)),
            AttentionRoutingRelated(resourceContextId));
    }

    /// <summary>An integration's routing override was cleared, so it follows the instance default again.</summary>
    public static AuditEvent AttentionRoutingOverrideCleared(string integrationId, string name, string conditionKey, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.AttentionRoutingOverrideCleared),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("conditionKey", ConditionKeyValue(conditionKey))),
            AttentionRoutingRelated(resourceContextId));
}
