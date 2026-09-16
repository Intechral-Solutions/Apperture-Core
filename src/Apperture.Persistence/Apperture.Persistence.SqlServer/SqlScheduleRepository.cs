using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for schedules. Epic 28.
/// </summary>
public sealed class SqlScheduleRepository : IScheduleRepository
{
    private readonly AppertureDbContext _db;

    public SqlScheduleRepository(AppertureDbContext db) => _db = db;

    public async ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken ct = default)
    {
        var e = await _db.ScheduleDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        return e is null ? null : ToDefinition(e);
    }

    public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken ct = default) =>
        ListDefinitionsAsync(resourceContextId: null, ct);

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(
        ResourceContextId? resourceContextId,
        CancellationToken ct = default)
    {
        var query = _db.ScheduleDefinitions.AsNoTracking().AsQueryable();

        // Context filtering happens in SQL, never by loading the table and filtering afterwards.
        if (resourceContextId is not null)
            query = query.Where(e => e.ResourceContextId == resourceContextId.Value);

        var entities = await query.ToListAsync(ct);
        return entities.Select(ToDefinition).ToList();
    }

    public async ValueTask<IReadOnlyList<ScheduleDefinition>> GetByIntegrationIdAsync(IntegrationId integrationId, CancellationToken ct = default)
    {
        var entities = await _db.ScheduleDefinitions.AsNoTracking()
            .Where(e => e.IntegrationId == integrationId.Value)
            .ToListAsync(ct);
        return entities.Select(ToDefinition).ToList();
    }

    public async ValueTask AddDefinitionAsync(ScheduleDefinition d, CancellationToken ct = default)
    {
        _db.ScheduleDefinitions.Add(ToDefinitionEntity(d));
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask UpdateDefinitionAsync(ScheduleDefinition d, CancellationToken ct = default)
    {
        var e = await _db.ScheduleDefinitions.FirstOrDefaultAsync(x => x.Id == d.Id.Value, ct)
            ?? throw new InvalidOperationException($"Schedule {d.Id.Value} not found.");
        e.Name = d.Name;
        e.RecurrenceExpression = d.RecurrenceExpression;
        e.TimeZone = d.TimeZone;
        e.Enabled = d.Enabled;
        e.TargetJobType = (int)d.Target.JobType;
        e.TargetJobConfigurationJson = d.Target.JobConfiguration;
        e.ExecutionPolicyRef = d.ExecutionPolicyRef;
        e.Version = d.Version;
        e.IntegrationId = d.IntegrationId?.Value;
        // Ownership is established at create time. CTX-1 has no move operation, so UpdateDefinitionAsync
        // deliberately does not rewrite ResourceContextId.
        if (d.Lifecycle is { } life)
        {
            e.LastScheduleEnabledAtUtc = life.LastScheduleEnabledAtUtc;
            e.LastScheduleDisabledAtUtc = life.LastScheduleDisabledAtUtc;
            e.MaterialScheduleSemanticsChangedAtUtc = life.MaterialScheduleSemanticsChangedAtUtc;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId planeId, CancellationToken ct = default)
    {
        var entities = await _db.ScheduleAssignments.AsNoTracking()
            .Where(x => x.RuntimePlaneId == planeId.Value)
            .ToListAsync(ct);
        return entities.Select(ToAssignment).ToList();
    }

    public async ValueTask<ScheduleAssignment?> GetAssignmentAsync(ScheduleAssignmentId id, CancellationToken ct = default)
    {
        var e = await _db.ScheduleAssignments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        return e is null ? null : ToAssignment(e);
    }

    public async ValueTask AddAssignmentAsync(ScheduleAssignment a, CancellationToken ct = default)
    {
        _db.ScheduleAssignments.Add(ToAssignmentEntity(a));
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask UpdateAssignmentAsync(ScheduleAssignment a, CancellationToken ct = default)
    {
        var e = await _db.ScheduleAssignments.FirstOrDefaultAsync(x => x.Id == a.Id.Value, ct)
            ?? throw new InvalidOperationException($"Assignment {a.Id.Value} not found.");
        e.PolicyOverrides = a.PolicyOverrides;
        e.DesiredVersion = a.DesiredVersion;
        e.AppliedVersion = a.AppliedVersion;
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<bool> RunExistsAsync(string scheduleId, DateTimeOffset fireTimeUtc, CancellationToken ct = default)
    {
        // SWM-2: compare DateTimeOffset to DateTimeOffset. EF SQL Server cannot translate
        // member access like FireTimeUtc.UtcDateTime inside the expression tree.
        var instant = NormalizeFireTimeUtc(fireTimeUtc);
        return await _db.ScheduleRuns.AsNoTracking()
            .AnyAsync(x => x.ScheduleId == scheduleId && x.FireTimeUtc == instant, ct);
    }

    public ValueTask<bool> TryClaimFireAsync(string scheduleId, DateTimeOffset fireTimeUtc, string assignmentId, string hostId, CancellationToken ct = default) =>
        TryClaimFireAsync(scheduleId, fireTimeUtc, assignmentId, hostId, resourceContextId: null, ct);

    /// <inheritdoc cref="IScheduleRepository.TryClaimFireAsync(string, DateTimeOffset, string, string, string?, CancellationToken)" />
    public async ValueTask<bool> TryClaimFireAsync(
        string scheduleId,
        DateTimeOffset fireTimeUtc,
        string assignmentId,
        string hostId,
        string? resourceContextId,
        CancellationToken ct = default)
    {
        var instant = NormalizeFireTimeUtc(fireTimeUtc);
        // InMemory does not reliably enforce unique indexes on DateTimeOffset; treat existing row as lost claim race.
        if (await RunExistsAsync(scheduleId, instant, ct))
            return false;

        try
        {
            _db.ScheduleRuns.Add(new ScheduleRunEntity
            {
                ScheduleId = scheduleId,
                AssignmentId = assignmentId,
                FireTimeUtc = instant,
                StartedAtUtc = DateTimeOffset.UtcNow,
                Outcome = "InProgress",
                OutcomeReason = null,
                RunId = null,
                ClaimedByHostId = hostId,
                // CTX-2: snapshotted at claim time, not at successful dispatch. Retained even if
                // dispatch subsequently fails — the fire still occurred in this ResourceContext.
                ResourceContextId = resourceContextId
            });
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return false;
        }
    }

    public async ValueTask<IReadOnlyList<ScheduleFireCandidate>> GetRecoverableStaleCandidatesAsync(RuntimePlaneId planeId, DateTimeOffset staleCutoffUtc, CancellationToken ct = default)
    {
        // Join definitions so disabled schedules cannot be re-dispatched via stale recovery.
        var runs = await (
            from r in _db.ScheduleRuns.AsNoTracking()
            join a in _db.ScheduleAssignments.AsNoTracking() on r.AssignmentId equals a.Id
            join d in _db.ScheduleDefinitions.AsNoTracking() on r.ScheduleId equals d.Id
            where a.RuntimePlaneId == planeId.Value
                  && d.Enabled
                  && r.Outcome == "InProgress"
                  && r.StartedAtUtc < staleCutoffUtc
            select new ScheduleFireCandidate(r.ScheduleId, r.AssignmentId, r.FireTimeUtc)
        ).ToListAsync(ct);

        return runs;
    }

    public async ValueTask<bool> TryRecoverStaleClaimAsync(string scheduleId, DateTimeOffset fireTimeUtc, string assignmentId, string hostId, DateTimeOffset staleCutoffUtc, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var instant = NormalizeFireTimeUtc(fireTimeUtc);
        var isInMemory = _db.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true;

        // InMemory provider does not support ExecuteUpdateAsync.
        if (!isInMemory)
        {
            var rows = await _db.ScheduleRuns
                .Where(r =>
                    r.ScheduleId == scheduleId &&
                    r.FireTimeUtc == instant &&
                    r.Outcome == "InProgress" &&
                    r.StartedAtUtc < staleCutoffUtc)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(r => r.ClaimedByHostId, hostId)
                        .SetProperty(r => r.StartedAtUtc, now),
                    ct);
            if (rows == 1)
                return true;
        }

        ScheduleRunEntity? run;
        if (isInMemory)
        {
            var candidates = await _db.ScheduleRuns
                .Where(r =>
                    r.ScheduleId == scheduleId &&
                    r.Outcome == "InProgress" &&
                    r.StartedAtUtc < staleCutoffUtc)
                .ToListAsync(ct);
            run = candidates.FirstOrDefault(r => r.FireTimeUtc == instant);
        }
        else
        {
            run = await _db.ScheduleRuns.FirstOrDefaultAsync(
                r =>
                    r.ScheduleId == scheduleId &&
                    r.FireTimeUtc == instant &&
                    r.Outcome == "InProgress" &&
                    r.StartedAtUtc < staleCutoffUtc,
                ct);
        }

        if (run is null)
            return false;
        run.ClaimedByHostId = hostId;
        run.StartedAtUtc = now;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc cref="IScheduleRepository.GetInProgressRunClaimAsync"/>
    public async ValueTask<(string ClaimedByHostId, DateTimeOffset StartedAtUtc, string? ResourceContextId)?> GetInProgressRunClaimAsync(string assignmentId, CancellationToken ct = default)
    {
        var run = await _db.ScheduleRuns.AsNoTracking()
            .Where(r => r.AssignmentId == assignmentId && r.Outcome == "InProgress")
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (run is null)
            return null;
        return (run.ClaimedByHostId ?? "", run.StartedAtUtc, run.ResourceContextId);
    }

    public async ValueTask CompleteRunAsync(string scheduleId, DateTimeOffset fireTimeUtc, string outcome, string? outcomeReason, string? runId, CancellationToken ct = default)
    {
        // SWM-2: same normalization as RunExistsAsync — no .UtcDateTime in the EF predicate.
        var instant = NormalizeFireTimeUtc(fireTimeUtc);
        var run = await _db.ScheduleRuns.FirstOrDefaultAsync(
            x => x.ScheduleId == scheduleId && x.FireTimeUtc == instant, ct);
        if (run is null)
            return;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        run.Outcome = outcome;
        run.OutcomeReason = outcomeReason;
        run.RunId = runId;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// UTC instant with zero offset, matching <c>ScheduleDispatchRunner</c> fire normalization
    /// and the unique index on <c>(ScheduleId, FireTimeUtc)</c>.
    /// </summary>
    private static DateTimeOffset NormalizeFireTimeUtc(DateTimeOffset fireTimeUtc) =>
        new(fireTimeUtc.UtcDateTime, TimeSpan.Zero);

    public async ValueTask UpdateStateAsync(string assignmentId, ScheduleStateUpdate state, CancellationToken ct = default)
    {
        var e = await _db.ScheduleStates.FirstOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
        if (e is null)
        {
            _db.ScheduleStates.Add(new ScheduleStateEntity
            {
                AssignmentId = assignmentId,
                NextDueUtc = state.NextDueUtc,
                LastAttemptedUtc = state.LastAttemptedUtc,
                LastOutcome = state.LastOutcome,
                LastOutcomeReason = state.LastOutcomeReason,
                LastSuccessfulDispatchUtc = state.LastSuccessfulDispatchUtc
            });
        }
        else
        {
            if (state.NextDueUtc.HasValue) e.NextDueUtc = state.NextDueUtc;
            if (state.LastAttemptedUtc.HasValue) e.LastAttemptedUtc = state.LastAttemptedUtc;
            if (state.LastOutcome is not null) e.LastOutcome = state.LastOutcome;
            if (state.LastOutcomeReason is not null) e.LastOutcomeReason = state.LastOutcomeReason;
            if (state.LastSuccessfulDispatchUtc.HasValue) e.LastSuccessfulDispatchUtc = state.LastSuccessfulDispatchUtc;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<ScheduleState?> GetStateAsync(string assignmentId, CancellationToken ct = default)
    {
        var e = await _db.ScheduleStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AssignmentId == assignmentId, ct);
        return e is null ? null : new ScheduleState(
            e.AssignmentId,
            e.NextDueUtc,
            e.LastAttemptedUtc,
            e.LastOutcome,
            e.LastOutcomeReason,
            e.LastSuccessfulDispatchUtc);
    }

    /// <inheritdoc />
    public async ValueTask<ScheduleDefinition?> GetByIdForUpdateAsync(ScheduleId id, CancellationToken cancellationToken = default)
    {
        List<ScheduleDefinitionEntity> rows;
        if (_db.Database.IsRelational())
        {
            // AsNoTracking is load-bearing: see the interface remarks on execution-strategy retries.
            // The UPDLOCK hint lives in the SQL text, so the lock is taken and held to end of transaction
            // regardless of tracking behavior.
            rows = await _db.ScheduleDefinitions
                .FromSqlInterpolated($"SELECT * FROM [ScheduleDefinitions] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id.Value}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        else
        {
            rows = await _db.ScheduleDefinitions.AsNoTracking()
                .Where(e => e.Id == id.Value)
                .ToListAsync(cancellationToken);
        }

        var entity = rows.SingleOrDefault();
        return entity is null ? null : ToDefinition(entity);
    }

    /// <inheritdoc />
    public async ValueTask SetResourceContextIdAsync(ScheduleId id, ResourceContextId resourceContextId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsRelational())
        {
            var affected = await _db.ScheduleDefinitions
                .Where(e => e.Id == id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResourceContextId, resourceContextId.Value), cancellationToken);
            if (affected == 0)
                throw new InvalidOperationException($"Schedule {id.Value} not found.");
            return;
        }

        // InMemory fallback: ExecuteUpdateAsync is unsupported there. Same precedent as
        // SqlScheduleRepository's existing ExecuteUpdateAsync/tracked-fallback pair (TryRecoverStaleClaimAsync).
        var entity = await _db.ScheduleDefinitions.FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {id.Value} not found.");
        entity.ResourceContextId = resourceContextId.Value;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static ScheduleDefinition ToDefinition(ScheduleDefinitionEntity e)
    {
        ScheduleLifecycleEvidence? life = null;
        if (e.LastScheduleEnabledAtUtc is not null
            || e.LastScheduleDisabledAtUtc is not null
            || e.MaterialScheduleSemanticsChangedAtUtc is not null)
        {
            life = new ScheduleLifecycleEvidence(
                e.LastScheduleEnabledAtUtc,
                e.LastScheduleDisabledAtUtc,
                e.MaterialScheduleSemanticsChangedAtUtc);
        }

        return new ScheduleDefinition(
            new ScheduleId(e.Id),
            e.Name,
            e.RecurrenceExpression,
            e.TimeZone,
            e.Enabled,
            new ScheduledTarget((ScheduledJobType)e.TargetJobType, e.TargetJobConfigurationJson),
            e.ExecutionPolicyRef,
            e.Version,
            e.IntegrationId is not null ? new IntegrationId(e.IntegrationId) : null,
            life)
        {
            ResourceContextId = new ResourceContextId(
                string.IsNullOrWhiteSpace(e.ResourceContextId) ? ResourceContextIds.SharedValue : e.ResourceContextId)
        };
    }

    private static ScheduleDefinitionEntity ToDefinitionEntity(ScheduleDefinition d)
    {
        var e = new ScheduleDefinitionEntity
        {
            Id = d.Id.Value,
            IntegrationId = d.IntegrationId?.Value,
            // Safety net for direct-repository callers. The authoritative ownership resolution
            // (standalone -> Shared; Integration-attached -> the Integration's context) lives in
            // IScheduleManagementService.CreateScheduleAsync.
            ResourceContextId = d.ResourceContextId?.Value ?? ResourceContextIds.SharedValue,
            Name = d.Name,
            RecurrenceExpression = d.RecurrenceExpression,
            TimeZone = d.TimeZone,
            Enabled = d.Enabled,
            TargetJobType = (int)d.Target.JobType,
            TargetJobConfigurationJson = d.Target.JobConfiguration,
            ExecutionPolicyRef = d.ExecutionPolicyRef,
            Version = d.Version
        };
        if (d.Lifecycle is { } life)
        {
            e.LastScheduleEnabledAtUtc = life.LastScheduleEnabledAtUtc;
            e.LastScheduleDisabledAtUtc = life.LastScheduleDisabledAtUtc;
            e.MaterialScheduleSemanticsChangedAtUtc = life.MaterialScheduleSemanticsChangedAtUtc;
        }

        return e;
    }

    private static ScheduleAssignment ToAssignment(ScheduleAssignmentEntity e) => new(
        new ScheduleAssignmentId(e.Id),
        new ScheduleId(e.ScheduleId),
        new RuntimePlaneId(e.RuntimePlaneId),
        e.PolicyOverrides,
        e.DesiredVersion,
        e.AppliedVersion);

    private static ScheduleAssignmentEntity ToAssignmentEntity(ScheduleAssignment a) => new()
    {
        Id = a.Id.Value,
        ScheduleId = a.ScheduleId.Value,
        RuntimePlaneId = a.RuntimePlaneId.Value,
        PolicyOverrides = a.PolicyOverrides,
        DesiredVersion = a.DesiredVersion,
        AppliedVersion = a.AppliedVersion
    };

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sql)
            return sql.Number is 2601 or 2627;
        return false;
    }
}
