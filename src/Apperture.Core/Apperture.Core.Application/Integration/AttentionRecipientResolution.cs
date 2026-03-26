using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Integration;

public sealed record AttentionRecipientResolution(
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
    TimeSpan? EscalationAfter,
    TimeSpan? ReminderInterval,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    string? IntegrationRowPrimaryOperationalTeamId,
    string? IntegrationRowEscalationOperationalTeamId);

public sealed record AttentionRoutingUpsertCommand(
    bool IsEnabled,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    TimeSpan? EscalationAfter,
    TimeSpan? ReminderInterval,
    string? PrimaryOperationalTeamId,
    string? EscalationOperationalTeamId);
