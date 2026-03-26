using System.Collections.Generic;

namespace Apperture.Management.Client;

public sealed record AttentionRoutingEffectiveDto(
    bool HasRoute,
    bool Enabled,
    IReadOnlyList<string> PrimaryRecipients,
    string PrimarySource,
    IReadOnlyList<string> EscalationRecipients,
    string EscalationSource,
    string? PrimaryOperationalTeamId,
    string? PrimaryOperationalTeamName,
    string? EscalationOperationalTeamId,
    string? EscalationOperationalTeamName,
    bool HasPrimaryRoute,
    bool HasEscalationRoute,
    string? PrimaryNoRouteReason,
    string? EscalationNoRouteReason,
    IReadOnlyList<string> ResolvedPrimaryPrincipalUserIds,
    IReadOnlyList<string> ResolvedEscalationPrincipalUserIds,
    IReadOnlyList<string> ResolvedPrimaryExternalRecipients,
    IReadOnlyList<string> ResolvedEscalationExternalRecipients,
    long? EscalationAfterTicks,
    long? ReminderIntervalTicks,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    string? IntegrationRowPrimaryOperationalTeamId,
    string? IntegrationRowEscalationOperationalTeamId);

public sealed record UpsertAttentionRoutingRequest(
    bool IsEnabled,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    long? EscalationAfterTicks,
    long? ReminderIntervalTicks,
    string? PrimaryOperationalTeamId,
    string? EscalationOperationalTeamId);
