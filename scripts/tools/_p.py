from pathlib import Path
p = Path(r"f:/Code/Apperture/Apperture-Core/src/Apperture.ControlPlane/Apperture.ControlPlane.Security/IntegrationScheduleDiagnosticsService.cs")
t = p.read_text(encoding="utf-8")
t = t.replace(
    "    private readonly IAttentionIncidentNotificationLedger _attentionIncidentNotificationLedger;\n    private readonly TimeProvider _timeProvider;",
    "    private readonly IAttentionIncidentNotificationLedger _attentionIncidentNotificationLedger;\n    private readonly IAttentionRecipientResolver _attentionRecipientResolver;\n    private readonly TimeProvider _timeProvider;",
)
t = t.replace(
    "        IAttentionIncidentNotificationLedger attentionIncidentNotificationLedger,\n        TimeProvider timeProvider)",
    "        IAttentionIncidentNotificationLedger attentionIncidentNotificationLedger,\n        IAttentionRecipientResolver attentionRecipientResolver,\n        TimeProvider timeProvider)",
)
t = t.replace(
    "        _attentionIncidentNotificationLedger = attentionIncidentNotificationLedger;\n        _timeProvider = timeProvider;",
    "        _attentionIncidentNotificationLedger = attentionIncidentNotificationLedger;\n        _attentionRecipientResolver = attentionRecipientResolver;\n        _timeProvider = timeProvider;",
)
old_ret = """        var notifications = await _attentionIncidentNotificationLedger
            .ListRecentForIncidentsAsync(incidentIds, RecentAttentionIncidentNotificationsCap, cancellationToken)
            .ConfigureAwait(false);

        return new IntegrationScheduleDiagnosticsProjection("""
new_ret = """        var notifications = await _attentionIncidentNotificationLedger
            .ListRecentForIncidentsAsync(incidentIds, RecentAttentionIncidentNotificationsCap, cancellationToken)
            .ConfigureAwait(false);

        var route = await _attentionRecipientResolver
            .ResolveAsync(integrationId, AttentionConditionKeys.ExpectedRunMissing, cancellationToken)
            .ConfigureAwait(false);
        var routingSummary = new AttentionRoutingDiagnosticsSummary(
            route.HasRoute,
            route.Enabled,
            route.PrimaryRecipients,
            route.PrimarySource,
            route.EscalationRecipients,
            route.EscalationSource,
            route.EscalationAfter,
            route.ReminderInterval,
            route.SendResolvedNotification,
            route.SuppressRemindersWhenAcknowledged,
            route.SuppressEscalationWhenAcknowledged);

        return new IntegrationScheduleDiagnosticsProjection("""
assert old_ret in t
t = t.replace(old_ret, new_ret, 1)
t = t.replace(
    "            incidents,\n            notifications);",
    "            incidents,\n            notifications,\n            routingSummary);",
    1,
)
p.write_text(t, encoding="utf-8")
print("patched diagnostics service")
