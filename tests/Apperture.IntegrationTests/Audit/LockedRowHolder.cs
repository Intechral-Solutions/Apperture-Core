using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// A second session that holds one row (<c>UPDLOCK, ROWLOCK</c>, as every locking writer takes it) in an open transaction, so a
/// test can pause a writer on that row and then change the row, as a competing delete or move would, before releasing it.
/// </summary>
internal sealed class LockedRowHolder : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    private LockedRowHolder(SqlConnection connection, SqlTransaction transaction, int sessionId)
    {
        _connection = connection;
        _transaction = transaction;
        SessionId = sessionId;
    }

    public int SessionId { get; }

    public static async Task<LockedRowHolder> TakeAsync(string connectionString, string table, string id)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = $"SELECT [Id] FROM [{table}] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id;";
            lockCommand.Parameters.AddWithValue("@id", id);
            (await lockCommand.ExecuteScalarAsync()).Should().NotBeNull("the row exists and is now held");
        }

        await using var spid = connection.CreateCommand();
        spid.Transaction = transaction;
        spid.CommandText = "SELECT CAST(@@SPID AS int);";
        return new LockedRowHolder(connection, transaction, (int)(await spid.ExecuteScalarAsync())!);
    }

    public async Task ExecuteAsync(string sql, params (string Name, string Value)[] parameters)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task CommitAsync()
    {
        await _transaction!.CommitAsync();
        _transaction = null;
    }

    /// <summary>
    /// Waits until the server itself lists a request, blocked by this session, waiting for a lock on a row of
    /// <paramref name="table"/>. Fails if the writer finishes first: a writer that did not wait did not take the lock.
    /// </summary>
    public async Task WaitUntilBlockedAsync(AuditSqlScratch scratch, string table, Task writer)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (writer.IsCompleted)
            {
                var detail = writer.IsFaulted ? writer.Exception!.GetBaseException().Message : "it completed";
                throw new Xunit.Sdk.XunitException($"The writer did not wait for the {table} row ({detail}).");
            }

            var blocked = await scratch.ScalarAsync<int>(
                $"""
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                INNER JOIN sys.dm_exec_requests AS r ON r.session_id = l.request_session_id
                INNER JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = N'WAIT' AND l.resource_database_id = DB_ID() AND l.resource_type = N'KEY'
                  AND OBJECT_NAME(p.object_id) = N'{table}' AND r.blocking_session_id = {SessionId};
                """);
            if (blocked > 0)
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException($"No request was seen waiting on the {table} row within 30 seconds.");
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
