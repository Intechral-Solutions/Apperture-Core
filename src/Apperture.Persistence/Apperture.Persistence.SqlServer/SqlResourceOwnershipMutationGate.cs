using System.Data;
using Apperture.Core.Application.ResourceContexts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Apperture.Persistence.SqlServer;

/// <inheritdoc cref="IResourceOwnershipMutationGate" />
public sealed class SqlResourceOwnershipMutationGate : IResourceOwnershipMutationGate
{
    /// <summary>The one and only ownership gate resource name. Never parameterize or extend this.</summary>
    public const string LockResource = "apperture:resource-ownership";

    /// <summary>
    /// Bounded by design. Long enough that a normal operator mutation behind another one never fails,
    /// short enough that a stuck transaction surfaces as a clear transient conflict instead of an
    /// indefinitely hanging request.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly AppertureDbContext _db;
    private readonly TimeSpan _timeout;

    public SqlResourceOwnershipMutationGate(AppertureDbContext db, TimeSpan? timeout = null)
    {
        _db = db;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <inheritdoc />
    public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (!_db.Database.IsRelational())
        {
            // EF Core InMemory has no lock manager and no transactions. Unit tests exercise ownership
            // business logic; serialization itself is only provable against real SQL Server, which is
            // what ResourceOwnershipMutationGateSqlServerTests and
            // ResourceOwnershipMoveConcurrencySqlServerTests do.
            return;
        }

        var transaction = _db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "The ownership mutation gate must be acquired inside an already-open transaction. " +
                "A 'Transaction'-owned application lock taken outside a transaction is released " +
                "immediately and guarantees nothing.");

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        // Must exceed the server-side @LockTimeout below so the app-lock timeout always wins the race;
        // otherwise ADO.NET's own 30s default command timeout could fire first and leak a raw, unwrapped
        // SqlException instead of the documented ResourceOwnershipBusyException.
        command.CommandTimeout = Math.Max((int)_timeout.TotalSeconds + 5, 30);
        command.CommandText =
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', " +
            "@LockOwner = N'Transaction', @LockTimeout = @lockTimeout;";

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        command.Parameters.Add(result);
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = LockResource });
        command.Parameters.Add(new SqlParameter("@lockTimeout", SqlDbType.Int) { Value = (int)_timeout.TotalMilliseconds });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // sp_getapplock: 0 = granted, 1 = granted after waiting, -1 = timeout, -2 = cancelled,
        // -3 = chosen as deadlock victim, -999 = parameter/call error.
        var code = result.Value is int value ? value : -999;
        if (code >= 0)
            return;

        if (code is -1 or -2 or -3)
        {
            throw new ResourceOwnershipBusyException(
                "Another ownership change is in progress. Wait a moment and try again.");
        }

        throw new InvalidOperationException(
            $"Failed to acquire the resource ownership mutation gate (code {code}).");
    }
}
