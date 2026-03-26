using Apperture.Core.Application.Integration;

namespace Apperture.Core.Application.Services;

/// <summary>Epic 69: durable record of a notification attempt or suppression decision.</summary>
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
    string? SubjectSnapshot);

/// <summary>Epic 69: persist and query notification evidence for attention incidents.</summary>
public interface IAttentionIncidentNotificationLedger
{
    ValueTask AppendAsync(AttentionIncidentNotificationWrite row, CancellationToken cancellationToken = default);

    /// <summary>True if an activation was already attempted (any non-suppressed row).</summary>
    ValueTask<bool> HasNonSuppressedActivationAttemptAsync(string attentionIncidentId, CancellationToken cancellationToken = default);

    /// <summary>Latest successful send time for activation or reminder (for reminder cadence).</summary>
    ValueTask<DateTimeOffset?> GetLatestSuccessfulActivationOrReminderSentUtcAsync(
        string attentionIncidentId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> HasSucceededActivationAsync(string attentionIncidentId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AttentionIncidentNotificationProjection>> ListRecentForIncidentsAsync(
        IReadOnlyList<string> attentionIncidentIds,
        int maxTotal,
        CancellationToken cancellationToken = default);
}

public sealed record AttentionNotificationSendRequest(string Recipient, string Subject, string PlainTextBody);

public sealed record AttentionNotificationSendResult(bool Succeeded, string? ProviderMessageId, string? FailureSummary);

/// <summary>Epic 69: email channel; implementations must not claim success without delivery.</summary>
public interface IAttentionNotificationSender
{
    ValueTask<AttentionNotificationSendResult> SendEmailAsync(
        AttentionNotificationSendRequest request,
        CancellationToken cancellationToken = default);
}
