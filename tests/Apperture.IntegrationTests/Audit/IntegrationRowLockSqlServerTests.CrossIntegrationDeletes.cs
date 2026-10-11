using System.Data.Common;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 3, work package 5 (finding L-1): integration deletes of different integrations, each with historical setting
/// overrides. The delete used to count the integration's overrides again after removing them. <c>PlatformSettingValues</c> has no
/// index that starts with the scope, so under locking READ COMMITTED that count read across the other integration's rows too, and
/// two deletes could each wait on rows the other had just removed and still held locked. Each delete now removes its overrides by
/// row id and reads nothing of the table afterwards. The cases run on a locking READ COMMITTED catalog and on a
/// READ_COMMITTED_SNAPSHOT catalog.
/// </summary>
/// <remarks>
/// <para><b>What is proved, and what is not.</b> The control case shows the removed statement does wait on another integration's
/// removed rows under locking READ COMMITTED, so the mechanism tests below are able to fail. The isolated case runs the real delete
/// against a stand-in session that holds another integration's removed override rows (and, unlike a real delete, no audit row), so
/// the only thing it could wait on is a settings row. The pair case runs two real deletes at the point where both hold what they
/// removed, and asserts that no statement that touches the settings table was a deadlock victim.</para>
/// <para>It does not claim audited units cannot deadlock. The audit recorder counts an operation's rows by an unindexed column after
/// saving them (AUD-1.1, tracked in the AUD-1.4 record), so two audited units that have each inserted a row can still wait on each
/// other there. The pair case therefore allows a retry whose failed statement is on the audit table and does not assert zero
/// retries.</para>
/// </remarks>
public sealed partial class IntegrationRowLockSqlServerTests
{
    private static readonly string[] HistoricalKeys = ["Sample.Historical.Alpha", "Sample.Historical.Beta", TextKey];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Control_the_removed_recount_waits_on_another_integrations_removed_rows_only_under_locking_read_committed(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var first = await NewIntegrationWithOverridesAsync(scratch);
        var second = await NewIntegrationWithOverridesAsync(scratch);

        await using var holder = await RemovedOverridesHolder.TakeAsync(scratch, second);

        // The statement the delete used to run after removing its own overrides, for the first integration.
        await using var reader = new SqlConnection(scratch.ConnectionString);
        await reader.OpenAsync();
        await using var recount = reader.CreateCommand();
        recount.CommandText =
            """
            SET LOCK_TIMEOUT 2000;
            SELECT COUNT(*) FROM PlatformSettingValues WHERE ScopeKind = @kind AND ScopeId = @scope;
            """;
        recount.Parameters.AddWithValue("@kind", PlatformSettingScopeConstants.IntegrationKind);
        recount.Parameters.AddWithValue("@scope", first);

        if (rcsi)
        {
            (await recount.ExecuteScalarAsync()).Should().Be(HistoricalKeys.Length, "row versioning readers do not wait on writers");
        }
        else
        {
            var wait = async () => await recount.ExecuteScalarAsync();
            (await wait.Should().ThrowAsync<SqlException>("the scan runs into the other integration's removed rows"))
                .Which.Number.Should().Be(1222, "lock request time out period exceeded");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_delete_finishes_while_another_integrations_removed_override_rows_are_held_locked(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var deleted = await NewIntegrationWithOverridesAsync(scratch);
        var other = await NewIntegrationWithOverridesAsync(scratch);

        var gate = new PauseAroundOverrideRemoval();
        gate.ReleaseAfterRemoval(); // this case does not stop after the removal
        var delete = Task.Run(() => DeleteIntegrationAsync(scratch, deleted, principal, gate));

        // The delete has classified (the classification reads every settings row, so it must come before the other side holds any).
        await gate.BeforeRemoval.WaitAsync(TimeSpan.FromSeconds(60));
        await using var holder = await RemovedOverridesHolder.TakeAsync(scratch, other);
        gate.ReleaseBeforeRemoval();

        // It removes its overrides and goes on to its commit. Nothing it runs may read the rows the holder removed: the holder has no
        // audit row, so a wait on a settings row is the only wait this delete could have. The loop ends when the delete finishes or
        // the server lists it waiting on one.
        var waitedOnSettings = await FinishesWithoutWaitingOnSettingsAsync(scratch, delete);

        await holder.CommitAsync();
        var outcome = await Capture(delete);

        waitedOnSettings.Should().BeFalse("removing an integration's overrides must not read another integration's rows");
        outcome.Should().BeNull();
        gate.Attempts.Should().Be(1);
        (await IntegrationExistsAsync(scratch, deleted)).Should().BeFalse();
        (await OverrideCountAsync(scratch, deleted)).Should().Be(0, "no override is left behind");
        (await DeleteRowsAsync(scratch, deleted)).Should().Be(1);
        (await SettingsRemovedAsync(scratch, deleted)).Should().Be(HistoricalKeys.Length);
        (await OverrideCountAsync(scratch, other)).Should().Be(0, "the holder's own removal committed");
        (await IntegrationExistsAsync(scratch, other)).Should().BeTrue("the holder stands in for a delete in flight and removes only settings");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_deletes_of_different_integrations_with_historical_overrides_commit_and_none_deadlocks_on_the_settings_table(bool rcsi)
    {
        var scratch = Scratch(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var first = await NewIntegrationWithOverridesAsync(scratch);
        var second = await NewIntegrationWithOverridesAsync(scratch);
        var bystander = await NewIntegrationWithOverridesAsync(scratch);

        var firstGate = new PauseAroundOverrideRemoval();
        var secondGate = new PauseAroundOverrideRemoval();
        var firstDelete = Task.Run(() => DeleteIntegrationAsync(scratch, first, principal, dbFactory: () => NewRecordingDb(scratch, firstGate)));
        var secondDelete = Task.Run(() => DeleteIntegrationAsync(scratch, second, principal, dbFactory: () => NewRecordingDb(scratch, secondGate)));

        // Both have classified, and neither has removed anything.
        await Task.WhenAll(firstGate.BeforeRemoval, secondGate.BeforeRemoval).WaitAsync(TimeSpan.FromSeconds(60));

        // Each removes its overrides and stops, holding the locks of the rows it removed. Then both go on at once.
        firstGate.ReleaseBeforeRemoval();
        await firstGate.AfterRemoval.WaitAsync(TimeSpan.FromSeconds(60));
        secondGate.ReleaseBeforeRemoval();
        await secondGate.AfterRemoval.WaitAsync(TimeSpan.FromSeconds(60));
        firstGate.ReleaseAfterRemoval();
        secondGate.ReleaseAfterRemoval();

        var outcomes = await Task.WhenAll(Capture(firstDelete), Capture(secondDelete));

        var deadlocked = firstGate.DeadlockedStatements.Concat(secondGate.DeadlockedStatements).ToList();
        _output.WriteLine(
            $"deadlock victims: {deadlocked.Count} ({string.Join(" || ", deadlocked.Select(s => s.ReplaceLineEndings(" ")))}); attempts: {firstGate.Attempts} and {secondGate.Attempts}; {scratch.ExecutionMarker}");
        deadlocked.Should().NotContain(
            s => s.Contains("PlatformSettingValues", StringComparison.Ordinal),
            "no deadlock victim was running a statement that reads the settings table");
        outcomes.Should().AllSatisfy(o => o.Should().BeNull("both deletes commit, after a rerun if one was a deadlock victim elsewhere"));

        foreach (var id in new[] { first, second })
        {
            (await IntegrationExistsAsync(scratch, id)).Should().BeFalse();
            (await OverrideCountAsync(scratch, id)).Should().Be(0, "no override is left behind");
            (await DeleteRowsAsync(scratch, id)).Should().Be(1, "one primary row, whatever the attempts");
            (await SettingsRemovedAsync(scratch, id)).Should().Be(HistoricalKeys.Length);
        }

        (await IntegrationExistsAsync(scratch, bystander)).Should().BeTrue();
        (await OverrideCountAsync(scratch, bystander)).Should().Be(HistoricalKeys.Length, "a third integration's overrides are not touched");
    }

    /// <summary>
    /// A context as <see cref="AuditSqlScratch.NewDb"/> builds one, except that its execution strategy tells the gate whenever it is
    /// asked to retry a deadlock victim, so a deadlock the strategy would otherwise retry unseen is recorded with the statement
    /// that was running.
    /// </summary>
    private static AppertureDbContext NewRecordingDb(AuditSqlScratch scratch, PauseAroundOverrideRemoval gate) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(scratch.ConnectionString, sql => sql.ExecutionStrategy(d => new DeadlockRecordingStrategy(d, gate)))
            .AddInterceptors(AuditRecorderRowGuardInterceptor.Instance, gate)
            .Options);

    /// <summary>The production retry (<c>EnableRetryOnFailure</c>) with a note to the gate of each deadlock victim it retries.</summary>
    private sealed class DeadlockRecordingStrategy(ExecutionStrategyDependencies dependencies, PauseAroundOverrideRemoval gate)
        : SqlServerRetryingExecutionStrategy(dependencies)
    {
        protected override bool ShouldRetryOn(Exception exception)
        {
            for (var inner = exception; inner is not null; inner = inner.InnerException)
            {
                if (inner is SqlException { Number: DeadlockVictim })
                {
                    gate.NoteDeadlockVictim();
                    break;
                }
            }

            return base.ShouldRetryOn(exception);
        }
    }

    /// <summary>An integration with one historical override per key: two with no definition, one with. Keys repeat across integrations, so the key-first index interleaves their rows.</summary>
    private static async Task<string> NewIntegrationWithOverridesAsync(AuditSqlScratch scratch)
    {
        var integrationId = await NewIntegrationAsync(scratch);
        foreach (var key in HistoricalKeys)
        {
            await scratch.ExecuteAsync(
                $"""
                INSERT INTO PlatformSettingValues (Id, [Key], ScopeKind, ScopeId, ValueJson, CreatedUtc, UpdatedUtc)
                VALUES (N'{PlatformSettingScopeConstants.IntegrationKind}:{integrationId}:{key}', N'{key}',
                        N'{PlatformSettingScopeConstants.IntegrationKind}', N'{integrationId}', N'"x"',
                        SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                """);
        }

        return integrationId;
    }

    private static Task<int> OverrideCountAsync(AuditSqlScratch scratch, string integrationId) =>
        scratch.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM PlatformSettingValues WHERE ScopeKind = N'{PlatformSettingScopeConstants.IntegrationKind}' AND ScopeId = N'{integrationId}';");

    /// <summary>
    /// Returns true if the server lists a request waiting for a lock on a <c>PlatformSettingValues</c> row before
    /// <paramref name="delete"/> finishes; false once it finishes without that. Polls the server's own wait list, with no fixed
    /// delay standing in for it. A wait on another table does not end the loop.
    /// </summary>
    private static async Task<bool> FinishesWithoutWaitingOnSettingsAsync(AuditSqlScratch scratch, Task delete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (delete.IsCompleted)
                return false;

            var waiting = await scratch.ScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.dm_tran_locks AS l
                INNER JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                WHERE l.request_status = N'WAIT'
                  AND l.resource_database_id = DB_ID()
                  AND l.resource_type IN (N'KEY', N'RID', N'PAGE')
                  AND OBJECT_NAME(p.object_id) = N'PlatformSettingValues';
                """);
            if (waiting > 0)
                return true;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("The delete neither finished nor waited on a settings row within 30 seconds.");
    }

    /// <summary>
    /// A session that has removed an integration's override rows and not committed, as a delete in flight would hold them, but
    /// holds nothing else.
    /// </summary>
    private sealed class RemovedOverridesHolder : IAsyncDisposable
    {
        private readonly SqlConnection _connection;
        private SqlTransaction? _transaction;

        private RemovedOverridesHolder(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public static async Task<RemovedOverridesHolder> TakeAsync(AuditSqlScratch scratch, string integrationId)
        {
            var connection = new SqlConnection(scratch.ConnectionString);
            await connection.OpenAsync();
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM PlatformSettingValues WHERE ScopeKind = @kind AND ScopeId = @scope;";
            delete.Parameters.AddWithValue("@kind", PlatformSettingScopeConstants.IntegrationKind);
            delete.Parameters.AddWithValue("@scope", integrationId);
            (await delete.ExecuteNonQueryAsync()).Should().Be(HistoricalKeys.Length);
            return new RemovedOverridesHolder(connection, transaction);
        }

        public async Task CommitAsync()
        {
            if (_transaction is null)
                return;
            await _transaction.CommitAsync();
            await _transaction.DisposeAsync();
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
                catch (InvalidOperationException)
                {
                    // The transaction already ended.
                }

                await _transaction.DisposeAsync();
            }

            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Stops a delete just before its first removal of setting rows and again just after it, holding the transaction (and so the
    /// locks of the rows removed) open at the second stop. Also counts the attempts (each begins with the integration row's locked
    /// read) and records the text of every statement that failed as a deadlock victim.
    /// </summary>
    private sealed class PauseAroundOverrideRemoval : DbCommandInterceptor
    {
        private const string RemovalStatement = "DELETE FROM [PlatformSettingValues]";

        private readonly TaskCompletionSource _beforeReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _beforeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _afterReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _afterRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _deadlocked = [];
        private string? _lastStatement;
        private int _beforePaused;
        private int _afterPaused;
        private int _attempts;

        public Task BeforeRemoval => _beforeReached.Task;

        public Task AfterRemoval => _afterReached.Task;

        public int Attempts => Volatile.Read(ref _attempts);

        public IReadOnlyList<string> DeadlockedStatements
        {
            get
            {
                lock (_deadlocked)
                    return [.. _deadlocked];
            }
        }

        public void ReleaseBeforeRemoval() => _beforeRelease.TrySetResult();

        public void ReleaseAfterRemoval() => _afterRelease.TrySetResult();

        private async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingCoreAsync(
            DbCommand command, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken)
        {
            if (command.CommandText.Contains(RemovalStatement, StringComparison.Ordinal) && Interlocked.Exchange(ref _beforePaused, 1) == 0)
            {
                _beforeReached.TrySetResult();
                await _beforeRelease.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }

            return result;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[Integrations] WITH (UPDLOCK", StringComparison.Ordinal))
                Interlocked.Increment(ref _attempts);

            if (command.CommandText.Contains(RemovalStatement, StringComparison.Ordinal) && Interlocked.Exchange(ref _afterPaused, 1) == 0)
            {
                _afterReached.TrySetResult();
                await _afterRelease.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }

            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref _lastStatement, command.CommandText);
            return ReaderExecutingCoreAsync(command, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref _lastStatement, command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Volatile.Write(ref _lastStatement, command.CommandText);
            return ValueTask.FromResult(result);
        }

        /// <summary>Called by the context's execution strategy when it is about to retry a deadlock victim: the statement last started on it is the one that lost.</summary>
        public void NoteDeadlockVictim()
        {
            lock (_deadlocked)
                _deadlocked.Add(Volatile.Read(ref _lastStatement) ?? "(no statement)");
        }
    }
}
