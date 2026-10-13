using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Changes the attention routing settings: the instance-wide default and an integration's per-condition override. Each writer is one
/// audited unit of work (the settings change and its audit row commit together, or neither does); a request that changes nothing
/// declares a no-op and records nothing.
/// </summary>
/// <remarks>
/// <para>An override has no foreign key to its integration, so the integration row lock is what keeps an integration delete from
/// counting its blockers, deleting, and leaving an override behind. Inside the unit that lock is the first statement the writers
/// take after the operation lock, then the caller's re-run authorization against the integration as locked (an ownership move holds
/// the same row), then everything else. The integration delete takes the same row first, so neither order of the two can leave an
/// override without its integration or a delete that missed a committed override.</para>
/// <para>What is recorded names the condition and the fields that changed. Team ids, durations and flag values are never recorded.</para>
/// </remarks>
public sealed class AttentionRoutingManagementService : IAttentionRoutingManagementService
{
    private readonly AppertureDbContext _db;
    private readonly IIntegrationRepository _integrations;
    private readonly IAttentionRecipientResolver _resolver;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public AttentionRoutingManagementService(
        AppertureDbContext db,
        IIntegrationRepository integrations,
        IAttentionRecipientResolver resolver,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _db = db;
        _integrations = integrations;
        _resolver = resolver;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Attention routing changes require the audited unit of work.");

    public ValueTask<AttentionRecipientResolution> GetEffectiveRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default) =>
        _resolver.ResolveAsync(integrationId, conditionKey, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit</b>, primary <c>attention_routing.default.updated</c>. Inside the attempt: the instance routing row for
    /// the condition (locked to the commit, so two writers of the default queue and each compares against the row as the other left
    /// it), the operational teams the command names, then the comparison. The command is normalized as it is stored (a blank team id
    /// is none, an id is trimmed), so setting what the row already holds is a no-op: nothing is written and nothing is recorded.
    /// Otherwise the row is updated and the event names the fields that changed. A condition with no default row is refused as
    /// before.</para>
    /// </remarks>
    public async ValueTask UpsertInstanceDefaultRoutingAsync(
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;
        await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.AttentionRoutingDefaultUpdated, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var row = await LockInstanceRowAsync(conditionKey, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Instance attention routing for condition '{conditionKey}' was not found.");

                await ValidateTeamIdsAsync(command.PrimaryOperationalTeamId, command.EscalationOperationalTeamId, ct)
                    .ConfigureAwait(false);

                var changed = ChangedFields(RoutingValues.Of(row), RoutingValues.Of(command));
                if (changed.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                AttachForUpdate(row);
                ApplyCommand(row, command);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                await transaction.StageAsync(AuditEvents.AttentionRoutingDefaultUpdated(conditionKey, changed), ct).ConfigureAwait(false);
                return true;
            },
            // Nothing is returned to the caller: a verified commit is the default having been updated.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>One audited unit.</b> The primary row is <c>attention_routing.override.created</c> when the integration had no
    /// override for the condition and <c>attention_routing.override.updated</c> when it had one, which is known only inside the
    /// transaction, so both are declared. Inside the attempt, in this order: the integration row (locked to the commit; an
    /// integration deleted first is refused here), the caller's re-run authorization against the integration as locked, the
    /// operational teams the command names, then the override row. An override is inserted with the integration's name and owning
    /// resource context as they are under the lock; an existing one is compared field by field, and a command that changes nothing
    /// is a no-op that writes and records nothing.</para>
    /// </remarks>
    public async ValueTask UpsertIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        AttentionRoutingUpsertCommand command,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null)
    {
        var unit = Unit;
        await unit.ExecuteAsync(
            new AuditedOperation(
                AuditActionNames.AttentionRoutingOverrideCreated,
                AuditActor.CurrentPrincipal,
                AuditActionNames.AttentionRoutingOverrideUpsertActions),
            async (transaction, ct) =>
            {
                // Untracked and locking: the integration is fixed until the commit.
                var integration = await _integrations.GetByIdForUpdateAsync(integrationId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Integration {integrationId.Value} was not found.");

                // The caller's check, run again now that the row cannot move: an ownership move that committed after the check
                // made before this call is seen here.
                if (reauthorize is not null)
                    await reauthorize(ct).ConfigureAwait(false);

                await ValidateTeamIdsAsync(command.PrimaryOperationalTeamId, command.EscalationOperationalTeamId, ct)
                    .ConfigureAwait(false);

                var row = await _db.IntegrationAttentionRoutings.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IntegrationId == integrationId.Value && x.ConditionKey == conditionKey, ct)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    row = new IntegrationAttentionRoutingEntity
                    {
                        Id = NewRoutingId(),
                        IntegrationId = integrationId.Value,
                        ConditionKey = conditionKey,
                    };
                    ApplyCommand(row, command);
                    _db.IntegrationAttentionRoutings.Add(row);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                    await transaction.StageAsync(
                        AuditEvents.AttentionRoutingOverrideCreated(
                            integrationId.Value, integration.DisplayName, conditionKey, integration.ResourceContextId.Value),
                        ct).ConfigureAwait(false);
                    return true;
                }

                var changed = ChangedFields(RoutingValues.Of(row), RoutingValues.Of(command));
                if (changed.Count == 0)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                AttachForUpdate(row);
                ApplyCommand(row, command);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                await transaction.StageAsync(
                    AuditEvents.AttentionRoutingOverrideUpdated(
                        integrationId.Value, integration.DisplayName, conditionKey, changed, integration.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                return true;
            },
            // Nothing is returned to the caller: a verified commit is the override having been written.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>CTX-3B Plan 6a: Integration hard-delete blocks on attention-routing overrides, and before this
    /// operation existed there was no way for an operator to clear that blocker.</para>
    /// <para><b>One audited unit</b>, primary <c>attention_routing.override.cleared</c>, with the lock order of
    /// <see cref="UpsertIntegrationRoutingAsync"/>: the integration row, the caller's re-run authorization, then the override. An
    /// override that is not there changes nothing, records nothing and reports false. After a lost acknowledgement a verified commit
    /// reports true: the override was removed.</para>
    /// </remarks>
    public async ValueTask<bool> ClearIntegrationRoutingAsync(
        IntegrationId integrationId,
        string conditionKey,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? reauthorize = null)
    {
        var unit = Unit;
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.AttentionRoutingOverrideCleared, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                // Untracked and locking: the integration is fixed until the commit.
                var integration = await _integrations.GetByIdForUpdateAsync(integrationId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Integration {integrationId.Value} was not found.");

                // The caller's check, run again now that the row cannot move.
                if (reauthorize is not null)
                    await reauthorize(ct).ConfigureAwait(false);

                var row = await _db.IntegrationAttentionRoutings.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.IntegrationId == integrationId.Value && x.ConditionKey == conditionKey, ct)
                    .ConfigureAwait(false);
                if (row is null)
                {
                    transaction.DeclareNoOp();
                    return false;
                }

                AttachForUpdate(row);
                _db.IntegrationAttentionRoutings.Remove(row);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                await transaction.StageAsync(
                    AuditEvents.AttentionRoutingOverrideCleared(
                        integrationId.Value, integration.DisplayName, conditionKey, integration.ResourceContextId.Value),
                    ct).ConfigureAwait(false);
                return true;
            },
            // A verified commit is the override having been removed, whatever the table now holds.
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The instance routing row of one condition, locked to the end of the transaction on SQL Server (the update lock lives in the
    /// SQL text, so it is taken whether or not the row is tracked). Untracked, so an execution-strategy retry never meets a stale
    /// entity; null when the condition has no default row.
    /// </summary>
    private async Task<InstanceAttentionRoutingEntity?> LockInstanceRowAsync(string conditionKey, CancellationToken cancellationToken)
    {
        List<InstanceAttentionRoutingEntity> rows;
        if (_db.Database.IsRelational())
        {
            var rowId = InstanceAttentionRoutingDefaults.RowId;
            rows = await _db.InstanceAttentionRoutings
                .FromSqlInterpolated(
                    $"SELECT * FROM [InstanceAttentionRoutings] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {rowId} AND [ConditionKey] = {conditionKey}")
                .AsNoTracking()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            rows = await _db.InstanceAttentionRoutings.AsNoTracking()
                .Where(x => x.Id == InstanceAttentionRoutingDefaults.RowId && x.ConditionKey == conditionKey)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        return rows.SingleOrDefault();
    }

    /// <summary>Starts tracking a row read untracked, as unchanged, so only the properties then set are written.</summary>
    private void AttachForUpdate(InstanceAttentionRoutingEntity row)
    {
        if (_db.InstanceAttentionRoutings.Local.FirstOrDefault(x => x.Id == row.Id) is { } tracked)
            _db.Entry(tracked).State = EntityState.Detached;
        _db.InstanceAttentionRoutings.Attach(row);
    }

    /// <inheritdoc cref="AttachForUpdate(InstanceAttentionRoutingEntity)"/>
    private void AttachForUpdate(IntegrationAttentionRoutingEntity row)
    {
        if (_db.IntegrationAttentionRoutings.Local.FirstOrDefault(x => x.Id == row.Id) is { } tracked)
            _db.Entry(tracked).State = EntityState.Detached;
        _db.IntegrationAttentionRoutings.Attach(row);
    }

    /// <summary>The names, in the closed set the audit row allows, of the fields that differ.</summary>
    private static List<string> ChangedFields(RoutingValues current, RoutingValues requested)
    {
        var changed = new List<string>();
        if (current.IsEnabled != requested.IsEnabled)
            changed.Add("enabled");
        if (current.SendResolvedNotification != requested.SendResolvedNotification)
            changed.Add("sendResolvedNotification");
        if (current.SuppressRemindersWhenAcknowledged != requested.SuppressRemindersWhenAcknowledged)
            changed.Add("suppressRemindersWhenAcknowledged");
        if (current.SuppressEscalationWhenAcknowledged != requested.SuppressEscalationWhenAcknowledged)
            changed.Add("suppressEscalationWhenAcknowledged");
        if (current.EscalationAfterTicks != requested.EscalationAfterTicks)
            changed.Add("escalationAfter");
        if (current.ReminderIntervalTicks != requested.ReminderIntervalTicks)
            changed.Add("reminderInterval");
        if (!string.Equals(current.PrimaryTeamId, requested.PrimaryTeamId, StringComparison.Ordinal))
            changed.Add("primaryTeam");
        if (!string.Equals(current.EscalationTeamId, requested.EscalationTeamId, StringComparison.Ordinal))
            changed.Add("escalationTeam");
        return changed;
    }

    /// <summary>A routing setting as it is stored: durations as ticks, a blank team id as none, an id trimmed.</summary>
    private readonly record struct RoutingValues(
        bool IsEnabled,
        bool SendResolvedNotification,
        bool SuppressRemindersWhenAcknowledged,
        bool SuppressEscalationWhenAcknowledged,
        long? EscalationAfterTicks,
        long? ReminderIntervalTicks,
        string? PrimaryTeamId,
        string? EscalationTeamId)
    {
        public static RoutingValues Of(AttentionRoutingUpsertCommand command) =>
            new(
                command.IsEnabled,
                command.SendResolvedNotification,
                command.SuppressRemindersWhenAcknowledged,
                command.SuppressEscalationWhenAcknowledged,
                command.EscalationAfter?.Ticks,
                command.ReminderInterval?.Ticks,
                NormalizeTeamId(command.PrimaryOperationalTeamId),
                NormalizeTeamId(command.EscalationOperationalTeamId));

        public static RoutingValues Of(InstanceAttentionRoutingEntity row) =>
            new(
                row.IsEnabled,
                row.SendResolvedNotification,
                row.SuppressRemindersWhenAcknowledged,
                row.SuppressEscalationWhenAcknowledged,
                row.EscalationAfterTicks,
                row.ReminderIntervalTicks,
                row.PrimaryOperationalTeamId,
                row.EscalationOperationalTeamId);

        public static RoutingValues Of(IntegrationAttentionRoutingEntity row) =>
            new(
                row.IsEnabled,
                row.SendResolvedNotification,
                row.SuppressRemindersWhenAcknowledged,
                row.SuppressEscalationWhenAcknowledged,
                row.EscalationAfterTicks,
                row.ReminderIntervalTicks,
                row.PrimaryOperationalTeamId,
                row.EscalationOperationalTeamId);
    }

    private static string? NormalizeTeamId(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    private static void ApplyCommand(IntegrationAttentionRoutingEntity row, AttentionRoutingUpsertCommand command)
    {
        var values = RoutingValues.Of(command);
        row.IsEnabled = values.IsEnabled;
        row.SendResolvedNotification = values.SendResolvedNotification;
        row.SuppressRemindersWhenAcknowledged = values.SuppressRemindersWhenAcknowledged;
        row.SuppressEscalationWhenAcknowledged = values.SuppressEscalationWhenAcknowledged;
        row.EscalationAfterTicks = values.EscalationAfterTicks;
        row.ReminderIntervalTicks = values.ReminderIntervalTicks;
        row.PrimaryOperationalTeamId = values.PrimaryTeamId;
        row.EscalationOperationalTeamId = values.EscalationTeamId;
    }

    private static void ApplyCommand(InstanceAttentionRoutingEntity row, AttentionRoutingUpsertCommand command)
    {
        var values = RoutingValues.Of(command);
        row.IsEnabled = values.IsEnabled;
        row.SendResolvedNotification = values.SendResolvedNotification;
        row.SuppressRemindersWhenAcknowledged = values.SuppressRemindersWhenAcknowledged;
        row.SuppressEscalationWhenAcknowledged = values.SuppressEscalationWhenAcknowledged;
        row.EscalationAfterTicks = values.EscalationAfterTicks;
        row.ReminderIntervalTicks = values.ReminderIntervalTicks;
        row.PrimaryOperationalTeamId = values.PrimaryTeamId;
        row.EscalationOperationalTeamId = values.EscalationTeamId;
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
