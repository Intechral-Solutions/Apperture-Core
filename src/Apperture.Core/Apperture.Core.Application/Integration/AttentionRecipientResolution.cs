using Apperture.Core.Domain.Integration;

namespace Apperture.Core.Application.Integration;

public sealed record AttentionRecipientResolution(
    bool HasRoute,
    bool Enabled,
    IReadOnlyList<string> PrimaryRecipients,
    string PrimarySource,
    IReadOnlyList<string> EscalationRecipients,
    string EscalationSource,
    TimeSpan? EscalationAfter,
    TimeSpan? ReminderInterval,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged);

public sealed record AttentionRoutingUpsertCommand(
    bool IsEnabled,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    TimeSpan? EscalationAfter,
    TimeSpan? ReminderInterval,
    IReadOnlyList<string> PrimaryRecipients,
    IReadOnlyList<string> EscalationRecipients);
