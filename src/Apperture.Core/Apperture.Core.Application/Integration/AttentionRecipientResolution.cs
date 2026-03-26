using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Integration;

public sealed record AttentionRecipientResolution(
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
    TimeSpan? EscalationAfter,
    TimeSpan? ReminderInterval,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    string? IntegrationRowPrimaryContactGroupId,
    string? IntegrationRowEscalationContactGroupId);

public sealed record AttentionRoutingUpsertCommand(
    bool IsEnabled,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    TimeSpan? EscalationAfter,
    TimeSpan? ReminderInterval,
    string? PrimaryContactGroupId,
    string? EscalationContactGroupId);
