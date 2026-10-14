namespace Apperture.Core.Application.Audit;

// SEC-2.1: the typed producer of the attention incident acknowledgement. The target is the incident's integration, with the
// integration's name as a snapshot and its owning resource context as the related reference; the incident is a field. The row says
// that a note was given and never what it said. Recipients and notification content are not fields, so they cannot be recorded.
public static partial class AuditEvents
{
    /// <summary>An operator acknowledged an active attention incident, and the acknowledgement committed.</summary>
    public static AuditEvent AttentionIncidentAcknowledged(
        string integrationId,
        string name,
        string incidentId,
        string conditionKey,
        DateTimeOffset incidentStartedAt,
        bool noteProvided,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.AttentionIncidentAcknowledged),
            Target(AuditTargetTypes.Integration, integrationId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("name", name),
                ("incidentId", incidentId),
                ("conditionKey", ConditionKeyValue(conditionKey)),
                ("incidentStartedAt", incidentStartedAt),
                ("noteProvided", noteProvided)),
            AttentionRoutingRelated(resourceContextId));
}
