using Apperture.Core.Application.AdministratorViability;
using Microsoft.Data.SqlClient;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// What a SQL Server test needs to see the global administrator-set gate from outside the writer under test: who waits for it,
/// whether anybody holds it, and another change to administrator access that holds it on purpose. The server's own lock list is the
/// witness, so a wait is shown, never assumed from a delay.
/// </summary>
internal static class AdministratorGateTestSupport
{
    public static Task<int> ApplicationLockWaitersAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID();");

    public static Task<int> RowLockWaitersAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type IN (N'KEY', N'RID', N'PAGE', N'OBJECT') AND request_status = N'WAIT' AND resource_database_id = DB_ID();");

    /// <summary>
    /// True once the server lists a request waiting for an application lock (the gate: the audited unit's operation locks are unique
    /// per operation, so nothing else can be waited for); false if <paramref name="request"/> completed first, which is a request
    /// that did not wait.
    /// </summary>
    public static async Task<bool> WaitsOnTheGateAsync(AuditSqlScratch scratch, Task request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (request.IsCompleted)
                return false;
            if (await ApplicationLockWaitersAsync(scratch) > 0)
                return true;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("The request neither completed nor was seen waiting for the gate within 30 seconds.");
    }

    /// <summary>Another change to administrator access, in a session of its own: holds the gate until the session ends.</summary>
    public static async Task<RawSession> HoldTheGateAsync(AuditSqlScratch scratch)
    {
        var holder = await RawSession.BeginAsync(scratch.ConnectionString);
        await holder.ExecuteAsync(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName
            + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0; IF @r < 0 THROW 50000, 'gate not taken', 1;");
        return holder;
    }

    /// <summary>Tries the gate from a session of its own without waiting: 0 or 1 means it was free, -1 means somebody holds it.</summary>
    public static async Task<int> ProbeGateAsync(AuditSqlScratch scratch)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0; SELECT @r;";
        command.Parameters.AddWithValue("@res", PlatformAdministratorSetLock.ResourceName);
        var result = (int)(await command.ExecuteScalarAsync())!;
        await transaction.RollbackAsync();
        return result;
    }
}
