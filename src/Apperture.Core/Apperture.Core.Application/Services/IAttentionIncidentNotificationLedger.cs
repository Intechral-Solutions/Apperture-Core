using Apperture.Core.Application.Integration;

namespace Apperture.Core.Application.Services;

public sealed record AttentionIncidentNotificationWrite(
    string Id,
    string AttentionIncidentId,
    string IntegrationId,
    string ConditionKey,
    string NotificationKind,
    string Channel,
    string Recipient,
    string Status,
    string? SuppressionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AttemptedAtUtc,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? FailedAtUtc,
    string? FailureSummary,
    string? ProviderMessageId,
    string? SubjectSnapshot,
    string? RecipientResolvedFrom,
    bool WasEscalation,
    bool EscalationThresholdApplied,
    bool SuppressedDueToAcknowledgment,
    string? ContactGroupId,
    string? ContactGroupNameSnapshot);

public interface IAttentionIncidentNotificationLedger
{
    ValueTask AppendAsync(AttentionIncidentNotificationWrite row, CancellationToken cancellationToken = default);

    ValueTask<bool> HasNonSuppressedActivationAttemptAsync(string attentionIncidentId, CancellationToken cancellationToken = default);

    ValueTask<DateTimeOffset?> GetLatestSuccessfulActivationOrReminderSentUtcAsync(
        string attentionIncidentId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasSucceededActivationAsync(string attentionIncidentId, CancellationToken cancellationToken = default);

    ValueTask<bool> HasNonSuppressedEscalationAttemptAsync(string attentionIncidentId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AttentionIncidentNotificationProjection>> ListRecentForIncidentsAsync(
        IReadOnlyList<string> attentionIncidentIds,
        int maxTotal,
        CancellationToken cancellationToken = default);
}

public sealed record AttentionNotificationSendRequest(string Recipient, string Subject, string PlainTextBody);

public sealed record AttentionNotificationSendResult(bool Succeeded, string? ProviderMessageId, string? FailureSummary);

public interface IAttentionNotificationSender
{
    ValueTask<AttentionNotificationSendResult> SendEmailAsync(
        AttentionNotificationSendRequest request,
        CancellationToken cancellationToken = default);
}
