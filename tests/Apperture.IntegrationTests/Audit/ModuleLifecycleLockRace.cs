using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A second session that holds the module lifecycle lock (the application lock every module install, enable, disable and uninstall
/// takes before it reads anything) in an open transaction, so a test can queue real writers behind it, as
/// <see cref="ManagedContactEmailLockRace"/> does for the contact email lock. The lock manager grants a queue of exclusive requests
/// in the order they arrived, so which transition commits first is fixed by the test and both orders can run on either isolation
/// mode.
/// </summary>
internal sealed class ModuleLifecycleLockRace : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    private ModuleLifecycleLockRace(SqlConnection connection, SqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public static async Task<ModuleLifecycleLockRace> HoldAsync(AuditSqlScratch scratch)
    {
        var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'apperture:module-lifecycle', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0; SELECT @r;";
        ((int)(await command.ExecuteScalarAsync())!).Should().BeGreaterThanOrEqualTo(0, "the lock is free before the race starts");
        return new ModuleLifecycleLockRace(connection, transaction);
    }

    /// <summary>
    /// Waits until the server lists exactly <paramref name="count"/> requests waiting for an application lock. Fails if one of the
    /// <paramref name="writers"/> finishes first: a writer that did not wait did not take the lock.
    /// </summary>
    public static async Task WaitForWaitersAsync(AuditSqlScratch scratch, int count, params Task[] writers)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var writer in writers)
            {
                if (writer.IsCompleted)
                {
                    var detail = writer.IsFaulted ? writer.Exception!.GetBaseException().Message : "it completed";
                    throw new Xunit.Sdk.XunitException($"A writer did not wait for the module lifecycle lock ({detail}).");
                }
            }

            var waiting = await scratch.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID();");
            if (waiting == count)
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException($"Did not see {count} request(s) waiting for the module lifecycle lock within 30 seconds.");
    }

    public async Task ReleaseAsync()
    {
        await _transaction!.CommitAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch
            {
                // The connection is closed next either way.
            }
        }

        await _connection.DisposeAsync();
    }
}
