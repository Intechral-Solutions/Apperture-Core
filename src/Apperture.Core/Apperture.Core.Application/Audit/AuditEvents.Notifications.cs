namespace Apperture.Core.Application.Audit;

// AUD-1.4 slice 6: the typed producers of the notification definition and subscription lifecycle, the managed contact directory and
// the diagnostic sends. The definition, subscription or contact is the target. A definition carries its name as a snapshot; its rule
// and template (free text) are never recorded, only the names of the parts that changed. A subscription names its recipient by id.
// A managed contact is a third party: its address, display name and directory identity have no field. A diagnostic send records
// what the platform observed, never delivery, and never an address, a subject, a body or the transport's error text.
public static partial class AuditEvents
{
    private static AuditRelatedReferences NotificationRelated(string? resourceContextId) =>
        new(ResourceContextId: BoundedId(resourceContextId, AuditLogResourceContextIdLength));

    // ---- Definitions ----

    /// <summary>A user-created notification definition was created.</summary>
    public static AuditEvent NotificationDefinitionCreated(
        string definitionId,
        string name,
        string? integrationId,
        bool enabled,
        bool toastEnabledByDefault,
        string? defaultSeverity,
        string? primaryEventType,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationDefinitionCreated),
            Target(AuditTargetTypes.NotificationDefinition, definitionId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("name", name),
                ("integrationId", BoundedId(integrationId)),
                ("enabled", enabled),
                ("toastEnabledByDefault", toastEnabledByDefault),
                ("defaultSeverity", BoundedId(defaultSeverity)),
                ("primaryEventType", BoundedId(primaryEventType))),
            NotificationRelated(resourceContextId));

    /// <summary>A definition's fields changed. <paramref name="changedFields"/> names the parts that differ, never their content.</summary>
    public static AuditEvent NotificationDefinitionUpdated(
        string definitionId,
        string name,
        string? integrationId,
        IReadOnlyList<string> changedFields,
        string? resourceContextId)
    {
        if (changedFields.Count == 0)
            throw new ArgumentException("An update that changes nothing is not audited as a change.", nameof(changedFields));
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationDefinitionUpdated),
            Target(AuditTargetTypes.NotificationDefinition, definitionId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("integrationId", BoundedId(integrationId)), ("changedFields", changedFields)),
            NotificationRelated(resourceContextId));
    }

    /// <summary>A definition was enabled or disabled by the enable request (a change that does not change the state is not audited).</summary>
    public static AuditEvent NotificationDefinitionEnableChanged(
        string definitionId, string name, string? integrationId, bool enabled, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(enabled ? AuditActionNames.NotificationDefinitionEnabled : AuditActionNames.NotificationDefinitionDisabled),
            Target(AuditTargetTypes.NotificationDefinition, definitionId),
            AuditOutcomeCodes.Succeeded,
            Fields(("name", name), ("integrationId", BoundedId(integrationId))),
            NotificationRelated(resourceContextId));

    /// <summary>A definition was deleted with its subscriptions, user opt-outs and per-user preferences (counted, not listed).</summary>
    public static AuditEvent NotificationDefinitionDeleted(
        string definitionId,
        string name,
        string? integrationId,
        int subscriptionsRemoved,
        int userOptOutsRemoved,
        int userPreferencesRemoved,
        string? resourceContextId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subscriptionsRemoved);
        ArgumentOutOfRangeException.ThrowIfNegative(userOptOutsRemoved);
        ArgumentOutOfRangeException.ThrowIfNegative(userPreferencesRemoved);
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationDefinitionDeleted),
            Target(AuditTargetTypes.NotificationDefinition, definitionId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("name", name),
                ("integrationId", BoundedId(integrationId)),
                ("subscriptionsRemoved", subscriptionsRemoved),
                ("userOptOutsRemoved", userOptOutsRemoved),
                ("userPreferencesRemoved", userPreferencesRemoved)),
            NotificationRelated(resourceContextId));
    }

    // ---- Subscriptions ----

    /// <summary>An administrator subscribed a recipient (identified by id, never by address) to a definition.</summary>
    public static AuditEvent NotificationSubscriptionAdded(
        string subscriptionId,
        string definitionId,
        string? integrationId,
        string recipientKind,
        string recipientRef,
        string deliveryMode,
        bool managedContactCreated,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationSubscriptionAdded),
            Target(AuditTargetTypes.NotificationSubscription, subscriptionId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("definitionId", BoundedId(definitionId)),
                ("integrationId", BoundedId(integrationId)),
                ("recipientKind", recipientKind),
                ("recipientRef", BoundedId(recipientRef)),
                ("deliveryMode", deliveryMode),
                ("managedContactCreated", managedContactCreated ? true : null)),
            NotificationRelated(resourceContextId));

    /// <summary>A subscription was removed.</summary>
    public static AuditEvent NotificationSubscriptionRemoved(
        string subscriptionId,
        string definitionId,
        string? integrationId,
        string recipientKind,
        string recipientRef,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationSubscriptionRemoved),
            Target(AuditTargetTypes.NotificationSubscription, subscriptionId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("definitionId", BoundedId(definitionId)),
                ("integrationId", BoundedId(integrationId)),
                ("recipientKind", recipientKind),
                ("recipientRef", BoundedId(recipientRef))),
            NotificationRelated(resourceContextId));

    /// <summary>A subscription was enabled, disabled or given another delivery mode (<paramref name="change"/>: enabled, disabled, delivery_mode).</summary>
    public static AuditEvent NotificationSubscriptionUpdated(
        string subscriptionId,
        string definitionId,
        string? integrationId,
        string change,
        string? deliveryMode,
        string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationSubscriptionUpdated),
            Target(AuditTargetTypes.NotificationSubscription, subscriptionId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("definitionId", BoundedId(definitionId)),
                ("integrationId", BoundedId(integrationId)),
                ("change", change),
                ("deliveryMode", deliveryMode)),
            NotificationRelated(resourceContextId));

    /// <summary>An administrator opted a user (optionally within one team) out of a definition. The target is the opt-out row.</summary>
    public static AuditEvent NotificationUserOptOutSet(
        string optOutId, string definitionId, string userId, string? operationalTeamId, string? resourceContextId) =>
        UserOptOutEvent(AuditActionNames.NotificationSubscriptionUserOptOutSet, optOutId, definitionId, userId, operationalTeamId, resourceContextId);

    /// <summary>An administrator removed a user's opt-out. The target is the opt-out row that was removed.</summary>
    public static AuditEvent NotificationUserOptOutRemoved(
        string optOutId, string definitionId, string userId, string? operationalTeamId, string? resourceContextId) =>
        UserOptOutEvent(AuditActionNames.NotificationSubscriptionUserOptOutRemoved, optOutId, definitionId, userId, operationalTeamId, resourceContextId);

    private static AuditEvent UserOptOutEvent(
        string action, string optOutId, string definitionId, string userId, string? operationalTeamId, string? resourceContextId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(action),
            Target(AuditTargetTypes.NotificationSubscription, optOutId),
            AuditOutcomeCodes.Succeeded,
            Fields(
                ("definitionId", BoundedId(definitionId)),
                ("userId", BoundedId(userId)),
                ("operationalTeamId", BoundedId(operationalTeamId))),
            NotificationRelated(resourceContextId));

    // ---- Managed contacts ----

    /// <summary>
    /// A managed contact was created. <paramref name="via"/> is admin, subscription or entra_import; <paramref name="source"/> custom
    /// or entra. Only whether it has an address is recorded, never the address or the name.
    /// </summary>
    public static AuditEvent ManagedContactCreated(string contactId, string source, string via, bool enabled, bool hasEmail) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ManagedContactCreated),
            Target(AuditTargetTypes.ManagedContact, contactId),
            AuditOutcomeCodes.Succeeded,
            Fields(("source", source), ("via", via), ("enabled", enabled), ("hasEmail", hasEmail)));

    /// <summary>A managed contact changed. <paramref name="changedFields"/> are drawn from displayName, email, enabled and entraSync.</summary>
    public static AuditEvent ManagedContactUpdated(string contactId, string source, IReadOnlyList<string> changedFields, bool enabled)
    {
        if (changedFields.Count == 0)
            throw new ArgumentException("An update that changes nothing is not audited as a change.", nameof(changedFields));
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ManagedContactUpdated),
            Target(AuditTargetTypes.ManagedContact, contactId),
            AuditOutcomeCodes.Succeeded,
            Fields(("source", source), ("changedFields", changedFields), ("enabled", enabled)));
    }

    /// <summary>A managed contact was linked to a platform user (the id of the user, and of the user it was linked to before).</summary>
    public static AuditEvent ManagedContactUserLinked(string contactId, string userId, string? previousUserId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ManagedContactUserLinked),
            Target(AuditTargetTypes.ManagedContact, contactId),
            AuditOutcomeCodes.Succeeded,
            Fields(("userId", BoundedId(userId)), ("previousUserId", BoundedId(previousUserId))));

    /// <summary>A managed contact was unlinked from the platform user it was linked to.</summary>
    public static AuditEvent ManagedContactUserUnlinked(string contactId, string previousUserId) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ManagedContactUserUnlinked),
            Target(AuditTargetTypes.ManagedContact, contactId),
            AuditOutcomeCodes.Succeeded,
            Fields(("previousUserId", BoundedId(previousUserId))));

    /// <summary>An Entra group's metadata was imported (created or refreshed). The target is the group's directory row.</summary>
    public static AuditEvent ManagedContactGroupImported(string groupRowId, bool created) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.ManagedContactGroupImported),
            Target(AuditTargetTypes.ManagedContact, groupRowId),
            AuditOutcomeCodes.Succeeded,
            Fields(("created", created)));

    // ---- Diagnostics ----

    /// <summary>
    /// A live SMTP test reached the transport. <paramref name="result"/> is what the transport reported: accepted (the server
    /// took the message; never delivery), rejected (the server refused a recipient or the message), failed (any other transport
    /// error after the attempt began) or no_answer (the call was cancelled, timed out or lost before an answer, so the message may or
    /// may not have left).
    /// </summary>
    public static AuditEvent NotificationSmtpTestAttempted(string result)
    {
        var outcome = result switch
        {
            "accepted" => AuditOutcomeCodes.Succeeded,
            "rejected" or "failed" => AuditOutcomeCodes.Failed,
            "no_answer" => AuditOutcomeCodes.Unknown,
            _ => throw new ArgumentException($"'{result}' is not a send result.", nameof(result)),
        };
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationSmtpTestAttempted),
            Target(AuditTargetTypes.Instance, AuditInstanceTargetId),
            outcome,
            Fields(("result", result)));
    }

    /// <summary>
    /// A live pipeline test created events, instances and outbox rows and, when <paramref name="dispatchRequested"/>, sent exactly
    /// the outbox rows it created. The counts are the dispatcher's own results for those rows. The outcome is <c>requested</c> when
    /// no send was made, <c>unknown</c> when any send's outcome is unknown, <c>failed</c> when sends were made and any was refused
    /// or failed, and <c>succeeded</c> when every send made was accepted (never delivery).
    /// </summary>
    public static AuditEvent NotificationPipelineTestAttempted(
        string definitionId,
        string? integrationId,
        bool dispatchRequested,
        int instancesCreated,
        int outboxRowsCreated,
        int sendsAttempted,
        int sendsAccepted,
        int sendsRejected,
        int sendsFailed,
        int sendsUnknown,
        string? resourceContextId)
    {
        foreach (var count in new[] { instancesCreated, outboxRowsCreated, sendsAttempted, sendsAccepted, sendsRejected, sendsFailed, sendsUnknown })
            ArgumentOutOfRangeException.ThrowIfNegative(count);
        var outcome = sendsAttempted == 0 ? AuditOutcomeCodes.Requested
            : sendsUnknown > 0 ? AuditOutcomeCodes.Unknown
            : sendsRejected + sendsFailed > 0 ? AuditOutcomeCodes.Failed
            : AuditOutcomeCodes.Succeeded;
        return AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationPipelineTestAttempted),
            Target(AuditTargetTypes.NotificationDefinition, definitionId),
            outcome,
            Fields(
                ("integrationId", BoundedId(integrationId)),
                ("dispatchRequested", dispatchRequested),
                ("instancesCreated", instancesCreated),
                ("outboxRowsCreated", outboxRowsCreated),
                ("sendsAttempted", sendsAttempted),
                ("sendsAccepted", sendsAccepted),
                ("sendsRejected", sendsRejected),
                ("sendsFailed", sendsFailed),
                ("sendsUnknown", sendsUnknown)),
            NotificationRelated(resourceContextId));
    }

    /// <summary>A diagnostic test was refused before any external call was made (<paramref name="test"/>: smtp or pipeline).</summary>
    public static AuditEvent NotificationDiagnosticsTestRefused(string test, string reason) =>
        AuditEvent.Create(
            AuditActionCatalog.Default.Get(AuditActionNames.NotificationDiagnosticsTestRefused),
            Target(AuditTargetTypes.Instance, AuditInstanceTargetId),
            AuditOutcomeCodes.Rejected,
            Fields(("test", test), ("reason", reason)));

    /// <summary>The platform instance, the target of a diagnostic that concerns no single entity.</summary>
    private const string AuditInstanceTargetId = "platform";
}
