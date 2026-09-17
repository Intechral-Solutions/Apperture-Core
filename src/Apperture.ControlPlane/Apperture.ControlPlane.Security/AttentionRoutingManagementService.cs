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

    public async ValueTask UpsertInstanceDefaultRoutingAsync(
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default)
    {
        await ValidateTeamIdsAsync(command.PrimaryOperationalTeamId, command.EscalationOperationalTeamId, cancellationToken)
            .ConfigureAwait(false);

        var row = await _db.InstanceAttentionRoutings
            .FirstOrDefaultAsync(
                x => x.Id == InstanceAttentionRoutingDefaults.RowId && x.ConditionKey == conditionKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            throw new InvalidOperationException($"Instance attention routing for condition '{conditionKey}' was not found.");

        ApplyCommand(row, command);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UpsertIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await _integrations.GetByIdAsync(integrationId, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException($"Integration {integrationId.Value} was not found.");

        await ValidateTeamIdsAsync(command.PrimaryOperationalTeamId, command.EscalationOperationalTeamId, cancellationToken)
            .ConfigureAwait(false);

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
        }

        ApplyCommand(row, command);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes an Integration's attention-routing override for one condition, reverting it to the instance
    /// default. Returns false when no override existed.
    /// </summary>
    /// <remarks>
    /// CTX-3B Plan 6a: Integration hard-delete blocks on attention-routing overrides, and before this
    /// operation existed there was no way for an operator to clear that blocker. The service previously
    /// exposed upsert only.
    /// </remarks>
    public async ValueTask<bool> ClearIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
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
            return false;

        _db.IntegrationAttentionRoutings.Remove(row);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static void ApplyCommand(IntegrationAttentionRoutingEntity row, AttentionRoutingUpsertCommand command)
    {
        row.IsEnabled = command.IsEnabled;
        row.SendResolvedNotification = command.SendResolvedNotification;
        row.SuppressRemindersWhenAcknowledged = command.SuppressRemindersWhenAcknowledged;
        row.SuppressEscalationWhenAcknowledged = command.SuppressEscalationWhenAcknowledged;
        row.EscalationAfterTicks = command.EscalationAfter?.Ticks;
        row.ReminderIntervalTicks = command.ReminderInterval?.Ticks;
        row.PrimaryOperationalTeamId = string.IsNullOrWhiteSpace(command.PrimaryOperationalTeamId)
            ? null
            : command.PrimaryOperationalTeamId.Trim();
        row.EscalationOperationalTeamId = string.IsNullOrWhiteSpace(command.EscalationOperationalTeamId)
            ? null
            : command.EscalationOperationalTeamId.Trim();
    }

    private static void ApplyCommand(InstanceAttentionRoutingEntity row, AttentionRoutingUpsertCommand command)
    {
        row.IsEnabled = command.IsEnabled;
        row.SendResolvedNotification = command.SendResolvedNotification;
        row.SuppressRemindersWhenAcknowledged = command.SuppressRemindersWhenAcknowledged;
        row.SuppressEscalationWhenAcknowledged = command.SuppressEscalationWhenAcknowledged;
        row.EscalationAfterTicks = command.EscalationAfter?.Ticks;
        row.ReminderIntervalTicks = command.ReminderInterval?.Ticks;
        row.PrimaryOperationalTeamId = string.IsNullOrWhiteSpace(command.PrimaryOperationalTeamId)
            ? null
            : command.PrimaryOperationalTeamId.Trim();
        row.EscalationOperationalTeamId = string.IsNullOrWhiteSpace(command.EscalationOperationalTeamId)
            ? null
            : command.EscalationOperationalTeamId.Trim();
    }

    private async Task ValidateTeamIdsAsync(
        string? primaryId,
        string? escalationId,
        CancellationToken cancellationToken)
    {
        foreach (var id in new[] { primaryId, escalationId })
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var exists = await _db.OperationalTeams.AsNoTracking()
                .AnyAsync(x => x.Id == id.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
                throw new InvalidOperationException($"Operational team '{id}' was not found.");
        }
    }

    private static string NewRoutingId() => $"attnrt_{Guid.NewGuid():N}";
}
