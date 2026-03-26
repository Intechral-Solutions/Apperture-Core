using System.Collections.Generic;

namespace Apperture.Management.Client;

public sealed record AttentionRoutingEffectiveDto(
    bool HasRoute,
    bool Enabled,
    IReadOnlyList<string> PrimaryRecipients,
    string PrimarySource,
    IReadOnlyList<string> EscalationRecipients,
    string EscalationSource,
    long? EscalationAfterTicks,
    long? ReminderIntervalTicks,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged);

public sealed record UpsertAttentionRoutingRequest(
    bool IsEnabled,
    bool SendResolvedNotification,
    bool SuppressRemindersWhenAcknowledged,
    bool SuppressEscalationWhenAcknowledged,
    long? EscalationAfterTicks,
    long? ReminderIntervalTicks,
    IReadOnlyList<string> PrimaryRecipients,
    IReadOnlyList<string> EscalationRecipients);
