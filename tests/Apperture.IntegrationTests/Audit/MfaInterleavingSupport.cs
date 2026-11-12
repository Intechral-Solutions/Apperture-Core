using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// Two-sided interleavings of native MFA writers against real SQL Server, for the replay-protection and lock-order suites. One side
/// is parked at a statement while it holds what it has taken; the other side runs until it is either finished or observed by the
/// server to be waiting for a lock (the dynamic management views, polled; never a fixed sleep); then the parked side is released.
/// Each side runs on a context of its own with a distinct application name (so the server can tell the sessions apart) and either
/// no retry at all, so a deadlock victim surfaces as SQL error 1205 to the test, or the production retry with a count of the
/// deadlock victims it retried.
/// </summary>
internal static class MfaInterleavingSupport
{
    public const string ParkedApplication = "Apperture.MfaInterleaving.Parked";
    public const string OtherApplication = "Apperture.MfaInterleaving.Other";

    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);

    /// <summary>What one side of an interleaving ended with.</summary>
    public sealed record Side(string Name, bool Succeeded, string Detail, Exception? Error, TimeSpan Elapsed, int RetriedDeadlockVictims)
    {
        public bool RaisedDeadlock => SqlErrorNumbers(Error).Contains(1205);

        /// <summary>A deadlock occurred on this side: surfaced as 1205, or retried away by the production strategy (and counted).</summary>
        public bool Deadlocked => RaisedDeadlock || RetriedDeadlockVictims > 0;

        public override string ToString() =>
            $"{Name}: succeeded={Succeeded} detail={Detail} elapsed={Elapsed.TotalMilliseconds:F0}ms raised1205={RaisedDeadlock} " +
            $"retriedDeadlockVictims={RetriedDeadlockVictims} error={Describe(Error)}";
    }

    /// <summary>How the other side stood when the parked side was released.</summary>
    public enum OtherState
    {
        /// <summary>The server reported the other side's session waiting for a lock.</summary>
        WaitingOnLock,

        /// <summary>The other side finished while the parked side still held its locks.</summary>
        Finished,
    }

    public sealed record Interleaving(Side Parked, Side Other, OtherState OtherWhenReleased, string RequestsWhileParked);

    public static Task<Side> RunSide(string name, RoleAuthorityWorld.DeadlockLog log, Func<Task<(bool Ok, string Detail)>> work) =>
        Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var (ok, detail) = await work();
                return new Side(name, ok, detail, null, clock.Elapsed, log.Victims);
            }
            catch (Exception ex)
            {
                return new Side(name, false, "threw", ex, clock.Elapsed, log.Victims);
            }
        });

    /// <summary>
    /// Waits until <paramref name="parked"/> reaches its pause, starts the other side, waits until the other side has finished or
    /// is waiting for a lock (as the server reports it), records the waiting requests, and releases the parked side.
    /// </summary>
    public static async Task<Interleaving> InterleaveAsync(
        AuditSqlScratch scratch,
        RoleAuthorityWorld.PauseAt pause,
        Task<Side> parked,
        Func<Task<Side>> startOther)
    {
        var first = await Task.WhenAny(pause.Reached, parked, Task.Delay(Patience));
        if (first == parked)
            throw new Xunit.Sdk.XunitException($"The parked operation finished without reaching the pause: {await parked}");
        if (first != pause.Reached)
            throw new TimeoutException("The parked operation did not reach the pause.");

        var other = startOther();
        var state = await WaitUntilWaitingOrFinishedAsync(scratch, OtherApplication, other);
        var requests = state == OtherState.WaitingOnLock ? await RequestsAsync(scratch) : "<other side finished>";
        pause.Release();

        var parkedSide = await parked.WaitAsync(Patience);
        var otherSide = await other.WaitAsync(Patience);
        return new Interleaving(parkedSide, otherSide, state, requests);
    }

    /// <summary>Polls the server until a session of <paramref name="application"/> waits for a lock, or <paramref name="operation"/> ends.</summary>
    public static async Task<OtherState> WaitUntilWaitingOrFinishedAsync(AuditSqlScratch scratch, string application, Task operation)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < Patience)
        {
            if (operation.IsCompleted)
                return OtherState.Finished;
            if (await LockWaitsAsync(scratch, application) > 0)
                return OtherState.WaitingOnLock;
            await Task.WhenAny(operation, Task.Delay(20));
        }

        throw new TimeoutException($"The operation of {application} neither finished nor waited for a lock.");
    }

    private static async Task<int> LockWaitsAsync(AuditSqlScratch scratch, string application)
    {
        await using var connection = new SqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
              FROM sys.dm_exec_requests AS r
              JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id
             WHERE s.program_name = @application AND r.database_id = DB_ID() AND r.wait_type LIKE N'LCK[_]%';
            """;
        command.Parameters.Add(new SqlParameter("@application", System.Data.SqlDbType.NVarChar, 128) { Value = application });
        return (int)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>The waiting requests of the catalog, from the dynamic management views only (no user table is read).</summary>
    private static async Task<string> RequestsAsync(AuditSqlScratch scratch)
    {
        var rows = await scratch.RowsAsync(
            """
            SELECT s.program_name, r.command, r.wait_type, r.wait_resource, r.blocking_session_id, LEFT(t.text, 120) AS sql_text
              FROM sys.dm_exec_requests AS r
              JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id
             CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
             WHERE r.database_id = DB_ID() AND r.session_id <> @@SPID;
            """);
        return rows.Count == 0
            ? "<no active request>"
            : string.Join(" | ", rows.Select(r => string.Join(", ", r.Select(kv => $"{kv.Key}={Flat(kv.Value)}"))));
    }

    /// <summary>
    /// A context like production's, on its own connection pool and under <paramref name="application"/>. With <paramref name="retry"/>
    /// it retries as production does and counts the deadlock victims it retried; without, a 1205 surfaces to the caller. Every
    /// session it opens sets a lock timeout, so a wait that is not a cycle ends as error 1222 instead of hanging.
    /// </summary>
    public static AppertureDbContext Db(
        AuditSqlScratch scratch, string application, bool retry, RoleAuthorityWorld.DeadlockLog log, params IInterceptor[] interceptors)
    {
        var connectionString = new SqlConnectionStringBuilder(scratch.ConnectionString) { ApplicationName = application }.ConnectionString;
        var builder = new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(connectionString, sql =>
            {
                if (retry)
                    sql.ExecutionStrategy(d => new RoleAuthorityWorld.DeadlockCountingStrategy(d, log));
            })
            .AddInterceptors([AuditRecorderRowGuardInterceptor.Instance, LockTimeoutInterceptor.Instance, .. interceptors]);
        return new AppertureDbContext(builder.Options);
    }

    /// <summary>One transaction under the context's execution strategy (so a retrying context retries it as production would).</summary>
    public static async Task OwnTransactionAsync(AppertureDbContext db, Func<Task> work)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await work();
            await transaction.CommitAsync();
        });
    }

    public static IReadOnlyList<int> SqlErrorNumbers(Exception? exception)
    {
        var numbers = new List<int>();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql)
                numbers.AddRange(sql.Errors.Cast<SqlError>().Select(x => x.Number));
            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    numbers.AddRange(SqlErrorNumbers(inner));
            }
        }

        return numbers;
    }

    public static string Describe(Exception? exception)
    {
        if (exception is null)
            return "none";
        var text = new StringBuilder();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (text.Length > 0)
                text.Append(" -> ");
            text.Append(e.GetType().Name);
            if (e is SqlException sql)
                text.Append('[').Append(string.Join(",", sql.Errors.Cast<SqlError>().Select(x => x.Number))).Append(']');
            var message = Flat(e.Message);
            text.Append(": ").Append(message.Length > 160 ? message[..160] : message);
        }

        return text.ToString();
    }

    public static string Flat(object? value) => (value?.ToString() ?? "NULL").Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>The server version and the catalog's isolation settings, for the test output.</summary>
    public static async Task<string> ServerAsync(AuditSqlScratch scratch, bool rcsi)
    {
        var rows = await scratch.RowsAsync(
            """
            SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)) AS version,
                   CAST(d.is_read_committed_snapshot_on AS int) AS rcsi_on,
                   CAST(DATABASEPROPERTYEX(DB_NAME(), 'IsOptimizedLockingOn') AS nvarchar(16)) AS optimized_locking
              FROM sys.databases AS d WHERE d.database_id = DB_ID();
            """);
        return $"server: rcsi parameter={rcsi} {string.Join(", ", rows.SelectMany(r => r).Select(kv => $"{kv.Key}={Flat(kv.Value)}"))}; {scratch.ExecutionMarker}";
    }

    /// <summary>SET LOCK_TIMEOUT 20000 on every session: a lock wait that is not a cycle ends as 1222, never a hang.</summary>
    private sealed class LockTimeoutInterceptor : DbConnectionInterceptor
    {
        public static readonly LockTimeoutInterceptor Instance = new();
        private const string Sql = "SET LOCK_TIMEOUT 20000;";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Sql;
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
