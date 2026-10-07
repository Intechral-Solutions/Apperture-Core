using System.Data;
using System.Data.Common;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Apperture.Persistence.SqlServer.Audit;

/// <summary>
/// The SQL Server audited unit of work (AUD-1 design gate §13.4, §13.5).
/// </summary>
/// <remarks>
/// <para><b>Attempt.</b> Inside the execution strategy: begin a transaction on the request's scoped context; take the
/// operation lock (<c>sp_getapplock</c>, exclusive, owner transaction) as the first statement; run the caller's reads,
/// validation, mutations and stages; save; check every staged row was inserted; commit. A failure before the commit call
/// cannot have committed anything, so the strategy may rerun it as usual.</para>
/// <para><b>Uncertain commit.</b> Whenever the commit call throws, whatever the exception, the attempt does not rerun.
/// Verification opens a fresh connection, takes the operation lock with session owner and a bounded wait, and reads the
/// primary row with a locking read. Row present with matching action and operation id: confirmed commit, and the result
/// is reconstructed from it. Row absent: confirmed rollback (the earlier attempt held the lock for its whole transaction,
/// so the grant proves that transaction ended), and only then may the operation rerun, with the same operation event id.
/// Lock not granted, or any failure while verifying: <see cref="AuditOutcomeUnknownException"/>.</para>
/// <para><b>Duplicate keys.</b> Only a violation of the <c>EventId</c> index on this operation's primary event id is a
/// candidate for prior success, and only after the same verification confirms the matching event. Any other unique
/// violation keeps its meaning and propagates; a duplicate on a secondary row's id is an internal error.</para>
/// </remarks>
public sealed class SqlAuditedUnitOfWork : IAuditedUnitOfWork
{
    private const string LockResourcePrefix = "apperture.audit-op.";

    private readonly AppertureDbContext _db;
    private readonly IPrincipalContext? _principals;
    private readonly AuditActionCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly AuditRecorderOptions _options;
    private readonly ILogger<SqlAuditedUnitOfWork> _logger;
    private readonly IAuditEventIdSource _eventIds;
    private readonly IAuditVerificationConnectionFactory _verificationConnections;

    public SqlAuditedUnitOfWork(
        AppertureDbContext db,
        IOptions<AuditRecorderOptions>? options = null,
        TimeProvider? time = null,
        ILogger<SqlAuditedUnitOfWork>? logger = null,
        IPrincipalContext? principals = null)
        : this(db, principals, AuditActionCatalog.Default, time, options?.Value, logger, null, null)
    {
    }

    internal SqlAuditedUnitOfWork(
        AppertureDbContext db,
        IPrincipalContext? principals,
        AuditActionCatalog catalog,
        TimeProvider? time,
        AuditRecorderOptions? options,
        ILogger<SqlAuditedUnitOfWork>? logger,
        IAuditEventIdSource? eventIds,
        IAuditVerificationConnectionFactory? verificationConnections)
    {
        _db = db;
        _principals = principals;
        _catalog = catalog;
        _time = time ?? TimeProvider.System;
        _options = options ?? new AuditRecorderOptions();
        _logger = logger ?? NullLogger<SqlAuditedUnitOfWork>.Instance;
        _eventIds = eventIds ?? GuidAuditEventIdSource.Instance;
        _verificationConnections = verificationConnections ?? SqlAuditVerificationConnectionFactory.Instance;
    }

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(
        AuditedOperation operation,
        Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
        Func<AuditCommittedOperation, CancellationToken, Task<T>> reconstruct,
        CancellationToken cancellationToken = default)
    {
        var primary = _catalog.Get(operation.PrimaryAction);
        foreach (var action in new[] { operation.PrimaryAction }.Concat(operation.AlternatePrimaryActions ?? []))
        {
            var candidate = _catalog.Get(action);
            if (candidate.WriteClass != AuditWriteClass.RequiredMutation || candidate.Details is null)
                throw new InvalidOperationException($"Audit action '{candidate.Action}' is not a converted W1 action and cannot identify an audited operation.");
        }

        var actor = await AuditRecorderRows.ResolveAsync(operation.Actor, _principals, cancellationToken).ConfigureAwait(false);

        // Generated once, before the execution strategy: stable across every attempt and every verification.
        var operationEventId = _eventIds.NewEventId();
        var strategy = _db.Database.CreateExecutionStrategy();
        var reruns = 0;
        while (true)
        {
            var result = await strategy.ExecuteAsync(
                ct => RunAttemptAsync(primary, operation, actor, operationEventId, attempt, ct),
                cancellationToken).ConfigureAwait(false);

            switch (result.Kind)
            {
                case AttemptKind.Committed:
                    return result.Value!;

                case AttemptKind.VerifiedCommit:
                    _logger.LogWarning(
                        "Audited operation {OperationEventId} ({Action}) had an uncertain commit; verification confirmed it committed.",
                        operationEventId,
                        primary.Action);
                    return await reconstruct(result.Committed!, cancellationToken).ConfigureAwait(false);

                case AttemptKind.VerifiedRollback when reruns < _options.MaxRerunsAfterConfirmedRollback:
                    reruns++;
                    _logger.LogWarning(
                        "Audited operation {OperationEventId} ({Action}) had an uncertain commit; verification confirmed a rollback. Rerunning ({Rerun}).",
                        operationEventId,
                        primary.Action,
                        reruns);
                    continue;

                default:
                    throw new AuditOperationNotCompletedException(operationEventId, result.Error);
            }
        }
    }

    private async Task<AttemptResult<T>> RunAttemptAsync<T>(
        AuditActionDefinition primary,
        AuditedOperation operation,
        ResolvedActor actor,
        string operationEventId,
        Func<IAuditedTransaction, CancellationToken, Task<T>> attempt,
        CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var unit = new AuditedTransaction(this, transaction, operation, actor, operationEventId);
        T value;
        try
        {
            await AcquireOperationLockAsync(transaction, operationEventId, cancellationToken).ConfigureAwait(false);
            value = await attempt(unit, cancellationToken).ConfigureAwait(false);
            unit.Seal();

            if (unit.IsNoOp)
            {
                // Nothing may persist without its audit row: an attempt that changed nothing commits nothing.
                await RollBackQuietlyAsync(transaction).ConfigureAwait(false);
                _db.ChangeTracker.Clear();
                return AttemptResult<T>.Done(value);
            }

            if (!unit.PrimaryStaged)
                throw new InvalidOperationException($"The audited operation expected a '{primary.Action}' row and staged none.");

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var inserted = await _db.AuditLogs.AsNoTracking()
                .CountAsync(a => a.OperationEventId == operationEventId, cancellationToken)
                .ConfigureAwait(false);
            if (inserted != unit.StagedCount)
                throw new InvalidOperationException($"The audited operation staged {unit.StagedCount} rows but {inserted} were inserted.");
        }
        catch (Exception ex)
        {
            await RollBackQuietlyAsync(transaction).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            if (AuditRecorderRows.IsEventIdViolation(ex))
                return await ResolveEventIdViolationAsync<T>(operation, unit, ex).ConfigureAwait(false);
            throw;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception commitError)
        {
            await DisposeQuietlyAsync(transaction).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            var verification = await VerifyAsync(operation, operationEventId).ConfigureAwait(false);
            return verification.Kind switch
            {
                VerificationKind.Committed => AttemptResult<T>.FromVerifiedCommit(verification.Row!),
                VerificationKind.RolledBack => AttemptResult<T>.FromVerifiedRollback(commitError),
                _ => throw new AuditOutcomeUnknownException(operationEventId, commitError),
            };
        }

        await DisposeQuietlyAsync(transaction).ConfigureAwait(false);
        return AttemptResult<T>.Done(value);
    }

    /// <summary>
    /// An <c>EventId</c> duplicate: a prior success only if the duplicated id is the primary's and verification confirms
    /// the matching event. A duplicate on a secondary id is an internal error.
    /// </summary>
    private async Task<AttemptResult<T>> ResolveEventIdViolationAsync<T>(AuditedOperation operation, AuditedTransaction unit, Exception violation)
    {
        bool primaryExists;
        try
        {
            await using var connection = await _verificationConnections
                .OpenAsync(_db.Database.GetConnectionString()!, CancellationToken.None).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM [AuditLogs] WITH (READCOMMITTEDLOCK) WHERE [EventId] = @eventId;";
            command.Parameters.Add(new SqlParameter("@eventId", SqlDbType.NVarChar, 64) { Value = unit.OperationEventId });
            primaryExists = (int)(await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false))! > 0;
        }
        catch (Exception ex)
        {
            throw new AuditOutcomeUnknownException(unit.OperationEventId, new AggregateException(violation, ex));
        }

        if (!primaryExists)
            throw new InvalidOperationException("A secondary audit event id collided with an existing row.", violation);

        var verification = await VerifyAsync(operation, unit.OperationEventId).ConfigureAwait(false);
        if (verification.Kind == VerificationKind.Committed)
            return AttemptResult<T>.FromVerifiedCommit(verification.Row!);
        throw new AuditOutcomeUnknownException(unit.OperationEventId, violation);
    }

    /// <summary>The verification procedure of §13.5, on a fresh connection.</summary>
    private async Task<Verification> VerifyAsync(AuditedOperation operation, string operationEventId)
    {
        var primaryAction = operation.PrimaryAction;
        try
        {
            await using var connection = await _verificationConnections
                .OpenAsync(_db.Database.GetConnectionString()!, CancellationToken.None).ConfigureAwait(false);

            var granted = await GetAppLockAsync(
                connection,
                transaction: null,
                LockResourcePrefix + operationEventId,
                owner: "Session",
                _options.VerificationLockWait,
                CancellationToken.None).ConfigureAwait(false);
            if (!granted)
            {
                _logger.LogError(
                    "Audited operation {OperationEventId} ({Action}): its operation lock was not granted within {Wait}; the earlier attempt may still be in progress. Outcome unknown.",
                    operationEventId,
                    primaryAction,
                    _options.VerificationLockWait);
                return Verification.Unknown;
            }

            AuditCommittedOperation? row = null;
            await using (var command = connection.CreateCommand())
            {
                // A locking read, never row versioning: the lock grant proved the earlier transaction ended.
                command.CommandText = """
                    SELECT [Action], [OperationEventId], [TargetType], [TargetId], [Details]
                    FROM [AuditLogs] WITH (READCOMMITTEDLOCK)
                    WHERE [EventId] = @eventId;
                    """;
                command.Parameters.Add(new SqlParameter("@eventId", SqlDbType.NVarChar, 64) { Value = operationEventId });
                await using var reader = await command.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
                if (await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    var action = reader.GetString(0);
                    var recordedOperation = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (!operation.AcceptsPrimary(action)
                        || !string.Equals(recordedOperation, operationEventId, StringComparison.Ordinal))
                    {
                        _logger.LogError(
                            "Audited operation {OperationEventId}: a row with its event id records a different action or operation. Outcome unknown.",
                            operationEventId);
                        return Verification.Unknown;
                    }

                    row = new AuditCommittedOperation(
                        operationEventId,
                        action,
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4));
                }
            }

            await ReleaseAppLockQuietlyAsync(connection, LockResourcePrefix + operationEventId).ConfigureAwait(false);
            return row is null ? Verification.RolledBack : Verification.Of(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audited operation {OperationEventId} ({Action}): verification failed. Outcome unknown.", operationEventId, primaryAction);
            return Verification.Unknown;
        }
    }

    private async Task AcquireOperationLockAsync(IDbContextTransaction transaction, string operationEventId, CancellationToken cancellationToken)
    {
        var granted = await GetAppLockAsync(
            (SqlConnection)_db.Database.GetDbConnection(),
            (SqlTransaction)transaction.GetDbTransaction(),
            LockResourcePrefix + operationEventId,
            owner: "Transaction",
            _options.OperationLockWait,
            cancellationToken).ConfigureAwait(false);
        if (!granted)
        {
            // Only an earlier attempt of this same operation can hold it, and that attempt never sent a commit (a commit
            // failure is verified, never rerun). Still, nothing is claimed here: the operation's fate is not established.
            throw new AuditOutcomeUnknownException(operationEventId);
        }
    }

    /// <summary>Requests an application lock; true when granted (return code 0 or 1).</summary>
    private static async Task<bool> GetAppLockAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string resource,
        string owner,
        TimeSpan wait,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = @owner, @LockTimeout = @timeout;";
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
        command.Parameters.Add(new SqlParameter("@owner", SqlDbType.NVarChar, 32) { Value = owner });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = (int)wait.TotalMilliseconds });
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(result);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return result.Value is int code && code >= 0;
    }

    private static async Task ReleaseAppLockQuietlyAsync(SqlConnection connection, string resource)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = N'Session';";
            command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource });
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Closing the connection ends the session and releases the lock anyway.
        }
    }

    private static async Task RollBackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A broken connection rolls the transaction back on the server.
        }

        await DisposeQuietlyAsync(transaction).ConfigureAwait(false);
    }

    private static async Task DisposeQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Disposal after a failed commit must not mask the verification outcome.
        }
    }

    private enum AttemptKind
    {
        Committed,
        VerifiedCommit,
        VerifiedRollback,
    }

    private sealed record AttemptResult<T>(AttemptKind Kind, T? Value, AuditCommittedOperation? Committed, Exception? Error)
    {
        public static AttemptResult<T> Done(T value) => new(AttemptKind.Committed, value, null, null);
        public static AttemptResult<T> FromVerifiedCommit(AuditCommittedOperation row) => new(AttemptKind.VerifiedCommit, default, row, null);
        public static AttemptResult<T> FromVerifiedRollback(Exception error) => new(AttemptKind.VerifiedRollback, default, null, error);
    }

    private enum VerificationKind
    {
        Committed,
        RolledBack,
        Unknown,
    }

    private sealed record Verification(VerificationKind Kind, AuditCommittedOperation? Row)
    {
        public static Verification RolledBack { get; } = new(VerificationKind.RolledBack, null);
        public static Verification Unknown { get; } = new(VerificationKind.Unknown, null);
        public static Verification Of(AuditCommittedOperation row) => new(VerificationKind.Committed, row);
    }

    /// <summary>One attempt's open transaction: stages complete rows into it.</summary>
    private sealed class AuditedTransaction(
        SqlAuditedUnitOfWork owner,
        IDbContextTransaction transaction,
        AuditedOperation operation,
        ResolvedActor resolved,
        string operationEventId) : IAuditedTransaction
    {
        private readonly AuditActor actor = resolved.Actor;

        private readonly HashSet<AuditEvent> _staged = new(ReferenceEqualityComparer.Instance);
        private bool _sealed;
        private bool _noOp;

        public string OperationEventId { get; } = operationEventId;
        public bool PrimaryStaged { get; private set; }
        public int StagedCount => _staged.Count;
        public bool IsNoOp => _noOp;

        public async ValueTask StageAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            var db = owner._db;
            if (_sealed || db.Database.CurrentTransaction?.TransactionId != transaction.TransactionId)
                throw new InvalidOperationException("Audit events can only be staged while this unit's transaction is current.");
            if (_noOp)
                throw new InvalidOperationException("An attempt that declared it changed nothing cannot stage audit events.");

            var definition = owner._catalog.Find(auditEvent.Action);
            if (definition is null || !ReferenceEquals(definition, auditEvent.Definition) && definition != auditEvent.Definition)
                throw new InvalidOperationException($"Audit action '{auditEvent.Action}' is not declared in the audit catalog.");
            if (definition.WriteClass != AuditWriteClass.RequiredMutation || definition.Details is null)
                throw new InvalidOperationException($"Audit action '{auditEvent.Action}' is not a W1 action with a structured producer.");
            if (!_staged.Add(auditEvent))
                throw new InvalidOperationException($"This '{auditEvent.Action}' event is already staged in this unit.");

            // The primary row carries the operation event id; a secondary row gets its own id per attempt.
            var isPrimary = !PrimaryStaged && operation.AcceptsPrimary(auditEvent.Action);
            var eventId = isPrimary ? OperationEventId : owner._eventIds.NewEventId();
            var username = await AuditRecorderRows.ReadUsernameAsync(db, actor, cancellationToken).ConfigureAwait(false);
            db.AuditLogs.Add(AuditRecorderRows.Build(
                auditEvent, actor, username, eventId, OperationEventId, owner._time.GetUtcNow(), resolved.ViaRecoveryAccess));
            if (isPrimary)
                PrimaryStaged = true;
        }

        public void DeclareNoOp()
        {
            if (_staged.Count > 0)
                throw new InvalidOperationException("An attempt that staged audit events cannot declare that it changed nothing.");
            _noOp = true;
        }

        public void Seal() => _sealed = true;
    }
}
