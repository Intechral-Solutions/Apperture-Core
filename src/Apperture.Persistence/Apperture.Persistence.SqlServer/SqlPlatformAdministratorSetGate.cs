using System.Data;
using Apperture.Core.Application.AdministratorViability;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Apperture.Persistence.SqlServer;

/// <inheritdoc cref="IPlatformAdministratorSetGate" />
/// <remarks>
/// Modelled on <see cref="SqlResourceOwnershipMutationGate"/>, and a separate gate from it: one name, one purpose, an ambient
/// transaction required, the server-side wait always shorter than the command's own timeout.
/// </remarks>
public sealed class SqlPlatformAdministratorSetGate : IPlatformAdministratorSetGate
{
    private readonly AppertureDbContext _db;
    private readonly TimeSpan _timeout;

    public SqlPlatformAdministratorSetGate(AppertureDbContext db, TimeSpan? timeout = null)
    {
        _db = db;
        _timeout = timeout ?? PlatformAdministratorSetLock.DefaultTimeout;
    }

    /// <inheritdoc />
    public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsRelational())
        {
            // EF Core InMemory has no lock manager and no transactions. Unit tests exercise the decision logic; serialization
            // itself is only provable against real SQL Server (PlatformAdministratorSetGateSqlServerTests).
            return;
        }

        var transaction = _db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The administrator-set gate must be acquired inside an already-open transaction. " +
                "A 'Transaction'-owned application lock taken outside a transaction is released " +
                "immediately and guarantees nothing.");

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        // The server-side @LockTimeout must always fire first, or ADO.NET's own command timeout could leak a raw SqlException
        // instead of the typed busy outcome.
        command.CommandTimeout = Math.Max((int)_timeout.TotalSeconds + 5, 30);
        command.CommandText =
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', " +
            "@LockOwner = N'Transaction', @LockTimeout = @lockTimeout;";

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(result);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = PlatformAdministratorSetLock.ResourceName });
        command.Parameters.Add(new SqlParameter("@lockTimeout", SqlDbType.Int) { Value = (int)_timeout.TotalMilliseconds });

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled the wait: that is the caller's decision, not contention and not a failure of the lock.
            throw new OperationCanceledException(cancellationToken);
        }

        var code = result.Value is int value ? value : (int?)null;
        var outcome = PlatformAdministratorSetLock.Interpret(code);
        if (PlatformAdministratorSetLock.IsHeld(outcome))
            return;

        if (outcome is PlatformAdministratorSetLockOutcome.TimedOut
            or PlatformAdministratorSetLockOutcome.Cancelled
            or PlatformAdministratorSetLockOutcome.DeadlockVictim)
        {
            throw new PlatformAdministratorSetBusyException(outcome);
        }

        throw new PlatformAdministratorSetGateException(code);
    }
}
