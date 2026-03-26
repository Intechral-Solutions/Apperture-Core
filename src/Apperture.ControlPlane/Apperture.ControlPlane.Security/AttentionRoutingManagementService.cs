using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

public sealed class AttentionRoutingManagementService : IAttentionRoutingManagementService
{
    private readonly AppertureDbContext _db;
    private readonly IIntegrationRepository _integrations;
    private readonly IAttentionRecipientResolver _resolver;

    public AttentionRoutingManagementService(
        AppertureDbContext db,
        IIntegrationRepository integrations,
        IAttentionRecipientResolver resolver)
    {
        _db = db;
        _integrations = integrations;
        _resolver = resolver;
    }

    public ValueTask<AttentionRecipientResolution> GetEffectiveRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default) =>
        _resolver.ResolveAsync(integrationId, conditionKey, cancellationToken);

    public async ValueTask UpsertIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await _integrations.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException($"Integration {integrationId.Value} was not found.");

        var row = await _db.IntegrationAttentionRoutings
            .FirstOrDefaultAsync(
                x => x.IntegrationId == integrationId.Value && x.ConditionKey == conditionKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new IntegrationAttentionRoutingEntity
            {
                Id = NewRoutingId(),
                IntegrationId = integrationId.Value,
                ConditionKey = conditionKey,
            };
            _db.IntegrationAttentionRoutings.Add(row);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var existing = await _db.AttentionRoutingRecipients
                .Where(r => r.IntegrationAttentionRoutingId == row.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            _db.AttentionRoutingRecipients.RemoveRange(existing);
        }

        row.IsEnabled = command.IsEnabled;
        row.SendResolvedNotification = command.SendResolvedNotification;
        row.SuppressRemindersWhenAcknowledged = command.SuppressRemindersWhenAcknowledged;
        row.SuppressEscalationWhenAcknowledged = command.SuppressEscalationWhenAcknowledged;
        row.EscalationAfterTicks = command.EscalationAfter?.Ticks;
        row.ReminderIntervalTicks = command.ReminderInterval?.Ticks;

        foreach (var a in Normalize(command.PrimaryRecipients))
        {
            _db.AttentionRoutingRecipients.Add(new AttentionRoutingRecipientEntity
            {
                Id = NewRecipientId(),
                IntegrationAttentionRoutingId = row.Id,
                Role = AttentionRoutingRecipientRoles.Primary,
                Address = a,
            });
        }

        foreach (var a in Normalize(command.EscalationRecipients))
        {
            _db.AttentionRoutingRecipients.Add(new AttentionRoutingRecipientEntity
            {
                Id = NewRecipientId(),
                IntegrationAttentionRoutingId = row.Id,
                Role = AttentionRoutingRecipientRoles.Escalation,
                Address = a,
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> Normalize(IReadOnlyList<string> raw) =>
        raw.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string NewRoutingId() => $"attnrt_{Guid.NewGuid():N}";
    private static string NewRecipientId() => $"attnrr_{Guid.NewGuid():N}";
}
