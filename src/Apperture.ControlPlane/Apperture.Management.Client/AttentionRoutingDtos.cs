using System.Collections.Generic;

namespace Apperture.Management.Client;

public sealed record AttentionRoutingEffectiveDto(
    bool HasRoute,
    bool Enabled,
    IReadOnlyList<string> PrimaryRecipients,
    string PrimarySource,
    IReadOnlyList<string> EscalationRecipients,
    string EscalationSource,
    string? PrimaryContactGroupId,
    string? PrimaryContactGroupName,
    string? EscalationContactGroupId,
    string? EscalationContactGroupName,
    bool HasPrimaryRoute,
    bool HasEscalationRoute,
    string? PrimaryNoRouteReason,
    string? EscalationNoRouteReason,
    long? EscalationAfterTicks,
    long? ReminderIntervalTicks,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    string? IntegrationRowPrimaryContactGroupId,
    string? IntegrationRowEscalationContactGroupId);

public sealed record UpsertAttentionRoutingRequest(
    bool IsEnabled,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    long? EscalationAfterTicks,
    long? ReminderIntervalTicks,
    string? PrimaryContactGroupId,
    string? EscalationContactGroupId);
