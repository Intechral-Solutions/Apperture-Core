using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Runtime;
using Apperture.Core.Domain.Workflow;
using Apperture.Core.Security.Trust;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Requests runtime shutdown (design gate §13.1 W1, §22 AUD-1.4). The existence and capability decision, the queued-request check,
/// the durable request and its audit row happen in one audited unit of work; the request records the unit's own actor as
/// <c>RequestedBy</c>, so the request row and the audit row share the actor by construction. The worker that later applies the
/// request is recorded separately, as <c>AppliedBy</c>.
/// </summary>
/// <remarks>
/// <para>The unit takes an update lock on the runtime identity row first, so concurrent requests for one runtime queue behind each
/// other and the decision is read under the lock. A request that finds a queued request returns it unchanged: nothing is written,
/// nothing is recorded, and the queued request keeps the actor that created it. The insert is saved inside the attempt so that a
/// lost race on the one-queued-request index (a writer that bypassed the lock) is a no-op that reports the winner; any other
/// duplicate propagates. A commit whose acknowledgement was lost returns the request read back by the id the audit row records.</para>
/// <para>The permission, existence and capability checks the caller makes before this service are unchanged.</para>
/// </remarks>
public sealed class RuntimeControlService : IRuntimeControlService
{
    private readonly AppertureDbContext _db;
    private readonly IRuntimeIdentityService _runtimeIdentity;
    private readonly IEnvironmentContext _environment;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public RuntimeControlService(
        AppertureDbContext db,
        IRuntimeIdentityService runtimeIdentity,
        IEnvironmentContext environment,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _db = db;
        _runtimeIdentity = runtimeIdentity;
        _environment = environment;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new AuditInvariantException("Runtime shutdown requests require the audited unit of work.");

    public async ValueTask<RuntimeControlRequestResult?> RequestShutdownAsync(
        string runtimeId,
        CancellationToken cancellationToken = default)
    {
        var unit = Unit;

        // Checked before the unit opens so a request for an unknown or ineligible runtime costs nothing; both are decided again
        // under the identity row's lock inside the unit.
        var runtime = await _runtimeIdentity.GetByIdAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        if (runtime is null)
            return null;

        // Capability prerequisite: runtime must declare the shutdown control capability.
        if (!runtime.DeclaredScopes.Contains(RuntimeCapabilityCatalog.ControlShutdown, StringComparer.Ordinal))
            return null;

        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.RuntimeShutdownRequestCreated, AuditActor.CurrentPrincipal),
            (transaction, ct) => RequestAsync(transaction, runtimeId, ct),
            ReconstructAsync,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RuntimeControlRequestResult?> RequestAsync(
        IAuditedTransaction transaction,
        string runtimeId,
        CancellationToken cancellationToken)
    {
        // The identity row is the lock: requests for one runtime queue here, and the decision below reads it under the lock.
        var identity = await LockRuntimeIdentityAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        if (identity is null
            || !SqlRuntimeIdentityService.DeserializeScopes(identity.DeclaredScopesJson)
                .Contains(RuntimeCapabilityCatalog.ControlShutdown, StringComparer.Ordinal))
        {
            transaction.DeclareNoOp();
            return null;
        }

        // Idempotency: a queued request is returned as it stands, with its own actor. Nothing changes, so nothing is recorded.
        var existing = await FindQueuedAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            transaction.DeclareNoOp();
            return Map(existing);
        }

        var request = new RuntimeControlRequestEntity
        {
            RuntimeId = runtimeId,
            ControlKind = RuntimeControlKindNames.Shutdown,
            Status = RuntimeControlStatusNames.Queued,
            RequestedAt = DateTimeOffset.UtcNow,
            RequestedBy = transaction.Actor.RecordedValue,
            AppliedAt = null,
            AppliedBy = null,
            LastError = null
        };
        _db.RuntimeControlRequests.Add(request);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (SqlUniqueViolation.IsOn(ex, SqlUniqueViolation.QueuedRuntimeControl))
        {
            // A writer that bypassed the identity lock queued a request first; ours rolled back to the savepoint. Report theirs,
            // and record nothing: nothing of ours committed.
            SqlUniqueViolation.Detach(_db, request);
            transaction.DeclareNoOp();
            var winner = await FindQueuedAsync(runtimeId, cancellationToken).ConfigureAwait(false);

            // The winner may have been applied between its commit and this read: no queued request is left to report, and nothing
            // of ours was written. That is the same answer as a request nothing can honour (the caller's rejection), not a fault.
            return winner is null ? null : Map(winner);
        }

        await transaction.StageAsync(
            AuditEvents.RuntimeShutdownRequestCreated(
                ScopeKeyConstants.RuntimeInstance(_environment.EnvironmentId, runtimeId),
                runtimeId,
                request.Id),
            cancellationToken).ConfigureAwait(false);
        return Map(request);
    }

    /// <summary>
    /// The runtime identity row, locked to the end of the transaction on SQL Server (the update lock lives in the SQL text, so it
    /// is taken whether or not the row is tracked). Untracked, so an execution-strategy retry never meets a stale entity.
    /// </summary>
    private async Task<RuntimeIdentityEntity?> LockRuntimeIdentityAsync(string runtimeId, CancellationToken cancellationToken)
    {
        List<RuntimeIdentityEntity> rows;
        if (_db.Database.IsRelational())
        {
            rows = await _db.RuntimeIdentities
                .FromSqlInterpolated($"SELECT * FROM [RuntimeIdentities] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {runtimeId}")
                .AsNoTracking()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            rows = await _db.RuntimeIdentities.AsNoTracking()
                .Where(x => x.Id == runtimeId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        return rows.SingleOrDefault();
    }

    private Task<RuntimeControlRequestEntity?> FindQueuedAsync(string runtimeId, CancellationToken cancellationToken) =>
        _db.RuntimeControlRequests.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.RuntimeId == runtimeId &&
                x.ControlKind == RuntimeControlKindNames.Shutdown &&
                x.Status == RuntimeControlStatusNames.Queued,
                cancellationToken);

    /// <summary>
    /// The request a confirmed commit created, read by the id its audit row records, with the actor that queued it. A request the
    /// worker has already applied is still reported as the request it was (its current state).
    /// </summary>
    private async Task<RuntimeControlRequestResult?> ReconstructAsync(AuditCommittedOperation committed, CancellationToken cancellationToken)
    {
        if (!long.TryParse(committed.GetField("requestId"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var requestId))
            throw new InvalidOperationException("The committed shutdown request records no request id.");

        var row = await _db.RuntimeControlRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken).ConfigureAwait(false);
        return row is null
            ? throw new InvalidOperationException($"The committed shutdown request {requestId} could not be read back.")
            : Map(row);
    }

    public async ValueTask<IReadOnlyList<RuntimeControlRequestResult>> GetShutdownRequestsAsync(
        string runtimeId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.RuntimeControlRequests.AsNoTracking()
            .Where(x =>
                x.RuntimeId == runtimeId &&
                x.ControlKind == RuntimeControlKindNames.Shutdown)
            .OrderByDescending(x => x.RequestedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    private static RuntimeControlRequestResult Map(RuntimeControlRequestEntity row) =>
        new(
            row.Id,
            row.RuntimeId,
            row.ControlKind,
            row.Status,
            row.RequestedAt,
            row.RequestedBy,
            row.AppliedAt,
            row.AppliedBy,
            row.LastError);
}
