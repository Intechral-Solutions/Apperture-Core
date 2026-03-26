using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlAttentionIncidentNotificationLedger : IAttentionIncidentNotificationLedger
{
    private readonly AppertureDbContext _db;

    public SqlAttentionIncidentNotificationLedger(AppertureDbContext db) => _db = db;

    public async ValueTask AppendAsync(
        AttentionIncidentNotificationWrite row,
        CancellationToken cancellationToken = default)
    {
        _db.AttentionIncidentNotifications.Add(new AttentionIncidentNotificationEntity
        {
            Id = row.Id,
            AttentionIncidentId = row.AttentionIncidentId,
            IntegrationId = row.IntegrationId,
            ConditionKey = row.ConditionKey,
            NotificationKind = row.NotificationKind,
            Channel = row.Channel,
            Recipient = row.Recipient,
            Status = row.Status,
            SuppressionReason = row.SuppressionReason,
            CreatedAtUtc = row.CreatedAtUtc,
            AttemptedAtUtc = row.AttemptedAtUtc,
            SentAtUtc = row.SentAtUtc,
            FailedAtUtc = row.FailedAtUtc,
            FailureSummary = row.FailureSummary,
            ProviderMessageId = row.ProviderMessageId,
            SubjectSnapshot = row.SubjectSnapshot,
            RecipientResolvedFrom = row.RecipientResolvedFrom,
            WasEscalation = row.WasEscalation,
            EscalationThresholdApplied = row.EscalationThresholdApplied,
            SuppressedDueToAcknowledgment = row.SuppressedDueToAcknowledgment,
            ContactGroupId = row.ContactGroupId,
            ContactGroupNameSnapshot = row.ContactGroupNameSnapshot,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> HasNonSuppressedActivationAttemptAsync(
        string attentionIncidentId,
        CancellationToken cancellationToken = default) =>
        await _db.AttentionIncidentNotifications.AsNoTracking()
            .AnyAsync(
                e => e.AttentionIncidentId == attentionIncidentId
                     && e.NotificationKind == AttentionNotificationKinds.Activated
                     && e.Status != AttentionNotificationStatuses.Suppressed,
                cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<DateTimeOffset?> GetLatestSuccessfulActivationOrReminderSentUtcAsync(
        string attentionIncidentId,
        CancellationToken cancellationToken = default)
    {
        var kinds = new[] { AttentionNotificationKinds.Activated, AttentionNotificationKinds.Reminder };
        var latest = await _db.AttentionIncidentNotifications.AsNoTracking()
            .Where(
                e => e.AttentionIncidentId == attentionIncidentId
                     && kinds.Contains(e.NotificationKind)
                     && e.Status == AttentionNotificationStatuses.Succeeded
                     && e.SentAtUtc != null)
            .OrderByDescending(e => e.SentAtUtc)
            .Select(e => e.SentAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return latest;
    }

    public async ValueTask<bool> HasSucceededActivationAsync(
        string attentionIncidentId,
        CancellationToken cancellationToken = default) =>
        await _db.AttentionIncidentNotifications.AsNoTracking()
            .AnyAsync(
                e => e.AttentionIncidentId == attentionIncidentId
                     && e.NotificationKind == AttentionNotificationKinds.Activated
                     && e.Status == AttentionNotificationStatuses.Succeeded,
                cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<bool> HasNonSuppressedEscalationAttemptAsync(
        string attentionIncidentId,
        CancellationToken cancellationToken = default) =>
        await _db.AttentionIncidentNotifications.AsNoTracking()
            .AnyAsync(
                e => e.AttentionIncidentId == attentionIncidentId
                     && e.NotificationKind == AttentionNotificationKinds.Escalation
                     && e.Status != AttentionNotificationStatuses.Suppressed,
                cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<IReadOnlyList<AttentionIncidentNotificationProjection>> ListRecentForIncidentsAsync(
        IReadOnlyList<string> attentionIncidentIds,
        int maxTotal,
        CancellationToken cancellationToken = default)
    {
        if (attentionIncidentIds.Count == 0 || maxTotal <= 0)
            return Array.Empty<AttentionIncidentNotificationProjection>();

        var idSet = attentionIncidentIds.ToHashSet(StringComparer.Ordinal);
        var rows = await _db.AttentionIncidentNotifications.AsNoTracking()
            .Where(n => idSet.Contains(n.AttentionIncidentId))
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(maxTotal)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(static e => new AttentionIncidentNotificationProjection(
            e.Id,
            e.AttentionIncidentId,
            e.IntegrationId,
            e.ConditionKey,
            e.NotificationKind,
            e.Channel,
            e.Recipient,
            e.Status,
            e.SuppressionReason,
            e.CreatedAtUtc,
            e.AttemptedAtUtc,
            e.SentAtUtc,
            e.FailedAtUtc,
            e.FailureSummary,
            e.ProviderMessageId,
            e.SubjectSnapshot,
            e.RecipientResolvedFrom,
            e.WasEscalation,
            e.EscalationThresholdApplied,
            e.SuppressedDueToAcknowledgment,
            e.ContactGroupId,
            e.ContactGroupNameSnapshot)).ToList();
    }
}
