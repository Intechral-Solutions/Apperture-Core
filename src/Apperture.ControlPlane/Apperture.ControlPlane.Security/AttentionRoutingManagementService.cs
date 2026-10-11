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
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null)
    {
        // AUD-1.4 slice 3: an override has no foreign key to its integration, so the integration row lock is what keeps an
        // integration delete from counting its blockers, deleting, and leaving this override behind. One transaction: the
        // integration row lock and existence check, the team check, the read that decides insert or update, the write, then the
        // commit that releases the lock. The execution strategy owns it, so a retry reruns every step in a new transaction.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            IntegrationAttentionRoutingEntity? row = null;
            try
            {
                // Untracked and locking: the integration is fixed until the commit.
                if (await _integrations.GetByIdForUpdateAsync(integrationId, cancellationToken).ConfigureAwait(false) is null)
                    throw new InvalidOperationException($"Integration {integrationId.Value} was not found.");

                // The caller's check, run again now that the row cannot move: an ownership move that committed after the check
                // made before this call is seen here.
                if (reauthorize is not null)
                    await reauthorize(cancellationToken).ConfigureAwait(false);

                await ValidateTeamIdsAsync(command.PrimaryOperationalTeamId, command.EscalationOperationalTeamId, cancellationToken)
                    .ConfigureAwait(false);

                row = await _db.IntegrationAttentionRoutings
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
                if (tx is not null)
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Only the row this attempt loaded or added: a rerun reads it again, and nothing else tracked on the context is
                // touched. A leftover Added row would otherwise be saved a second time beside the rerun's own.
                if (row is not null)
                    _db.Entry(row).State = EntityState.Detached;
                throw;
            }

            return true;
        }).ConfigureAwait(false);
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
