using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Notifications.Instances;
using Apperture.Core.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNotificationInstanceStore : INotificationInstanceStore
{
    private readonly AppertureDbContext _db;

    public SqlNotificationInstanceStore(AppertureDbContext db) => _db = db;

    public async ValueTask<NotificationInstanceCreateResult> TryCreateInstanceAsync(
        NotificationInstanceCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.NotificationInstances.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.NotificationDefinitionId == command.NotificationDefinitionId
                     && x.InstanceDedupeKey == command.InstanceDedupeKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
            return new NotificationInstanceCreateResult(ToDomain(existing), Created: false);

        var id = NotificationInstanceId.New();
        var now = DateTimeOffset.UtcNow;
        var entity = new NotificationInstanceEntity
        {
            Id = id.Value,
            NotificationDefinitionId = command.NotificationDefinitionId,
            NotificationKind = command.NotificationKind,
            Status = command.Status,
            PlatformEventId = command.PlatformEventId,
            AttentionIncidentId = command.AttentionIncidentId,
            IntegrationId = command.IntegrationId,
            ModuleId = command.ModuleId,
            RunId = command.RunId,
            ScheduleId = command.ScheduleId,
            InstanceDedupeKey = command.InstanceDedupeKey,
            RenderedSubject = command.RenderedSubject,
            RenderedSummary = command.RenderedSummary,
            RenderedBodyText = command.RenderedBodyText,
            RenderedBodyHtml = command.RenderedBodyHtml,
            RenderedActionText = command.RenderedActionText,
            RenderedActionUrl = command.RenderedActionUrl,
            RenderedContentFieldsJson = command.RenderedContentFieldsJson,
            TemplateVersionHash = command.TemplateVersionHash,
            RenderContextJson = command.RenderContextJson,
            RecipientSnapshotJson = command.RecipientSnapshotJson,
            SuppressionReason = command.SuppressionReason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _db.NotificationInstances.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new NotificationInstanceCreateResult(ToDomain(entity), Created: true);
        }
        catch (DbUpdateException ex) when (IsDedupeViolation(ex))
        {
            var raced = await _db.NotificationInstances.AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.NotificationDefinitionId == command.NotificationDefinitionId
                         && x.InstanceDedupeKey == command.InstanceDedupeKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (raced is not null)
                return new NotificationInstanceCreateResult(ToDomain(raced), Created: false);
            throw;
        }
    }

    public async ValueTask AddOutboxRowsAsync(
        NotificationInstanceId instanceId,
        IReadOnlyList<NotificationDeliveryOutboxCreateCommand> rows,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            var exists = await _db.NotificationDeliveryOutbox.AsNoTracking()
                .AnyAsync(
                    x => x.NotificationInstanceId == instanceId.Value
                         && x.RecipientEmail == row.RecipientEmail
                         && x.Channel == row.Channel,
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists)
                continue;

            _db.NotificationDeliveryOutbox.Add(new NotificationDeliveryOutboxEntity
            {
                Id = NotificationDeliveryOutboxId.New().Value,
                NotificationInstanceId = instanceId.Value,
                RecipientEmail = row.RecipientEmail,
                RecipientDisplayName = row.RecipientDisplayName,
                RecipientKind = row.RecipientKind,
                RecipientSource = row.RecipientSource,
                Channel = row.Channel,
                Status = row.Status,
                AttemptCount = 0,
                NextAttemptAtUtc = null,
                LastError = null,
                SentAtUtc = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsOutboxUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
        }
    }

    public async ValueTask<NotificationInstance?> GetByIdAsync(
        NotificationInstanceId id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.NotificationInstances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async ValueTask<IReadOnlyList<NotificationInstance>> ListAsync(
        NotificationInstanceListQuery query,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 1, 200);
        var q = _db.NotificationInstances.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.IntegrationId))
            q = q.Where(x => x.IntegrationId == query.IntegrationId);
        if (!string.IsNullOrWhiteSpace(query.NotificationDefinitionId))
            q = q.Where(x => x.NotificationDefinitionId == query.NotificationDefinitionId);
        if (!string.IsNullOrWhiteSpace(query.AttentionIncidentId))
            q = q.Where(x => x.AttentionIncidentId == query.AttentionIncidentId);

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToList();
    }

    public async ValueTask<IReadOnlyList<NotificationDeliveryOutbox>> ListOutboxForInstanceAsync(
        NotificationInstanceId instanceId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.NotificationDeliveryOutbox.AsNoTracking()
            .Where(x => x.NotificationInstanceId == instanceId.Value)
            .OrderBy(x => x.RecipientEmail)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToOutboxDomain).ToList();
    }

    public async ValueTask<bool> HasNonSuppressedActivationInstanceAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default) =>
        await _db.NotificationInstances.AsNoTracking()
            .AnyAsync(
                x => x.AttentionIncidentId == attentionIncidentId
                     && x.NotificationDefinitionId == notificationDefinitionId
                     && x.NotificationKind == NotificationKinds.Activation
                     && x.Status != NotificationInstanceStatuses.Suppressed,
                cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<DateTimeOffset?> GetLatestActivationOrReminderCreatedUtcAsync(
        string attentionIncidentId,
        string notificationDefinitionId,
        CancellationToken cancellationToken = default)
    {
        string[] kinds = [NotificationKinds.Activation, NotificationKinds.Reminder];
        return await _db.NotificationInstances.AsNoTracking()
            .Where(
                x => x.AttentionIncidentId == attentionIncidentId
                     && x.NotificationDefinitionId == notificationDefinitionId
                     && kinds.Contains(x.NotificationKind))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => (DateTimeOffset?)x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsDedupeViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("IX_NotificationInstances", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains(
            "NotificationDefinitionId",
            StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsOutboxUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("IX_NotificationDeliveryOutbox", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains(
            "NotificationInstanceId",
            StringComparison.OrdinalIgnoreCase) == true;

    private static NotificationInstance ToDomain(NotificationInstanceEntity e) =>
        new(
            NotificationInstanceId.From(e.Id),
            e.NotificationDefinitionId,
            e.NotificationKind,
            e.Status,
            e.PlatformEventId,
            e.AttentionIncidentId,
            e.IntegrationId,
            e.ModuleId,
            e.RunId,
            e.ScheduleId,
            e.InstanceDedupeKey,
            e.RenderedSubject,
            e.RenderedSummary,
            e.RenderedBodyText,
            e.RenderedBodyHtml,
            e.RenderedActionText,
            e.RenderedActionUrl,
            e.RenderedContentFieldsJson,
            e.TemplateVersionHash,
            e.RenderContextJson,
            e.RecipientSnapshotJson,
            e.SuppressionReason,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);

    private static NotificationDeliveryOutbox ToOutboxDomain(NotificationDeliveryOutboxEntity e) =>
        new(
            NotificationDeliveryOutboxId.From(e.Id),
            NotificationInstanceId.From(e.NotificationInstanceId),
            e.RecipientEmail,
            e.RecipientDisplayName,
            e.RecipientKind,
            e.RecipientSource,
            e.Channel,
            e.Status,
            e.AttemptCount,
            e.NextAttemptAtUtc,
            e.LastError,
            e.SentAtUtc,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);
}
