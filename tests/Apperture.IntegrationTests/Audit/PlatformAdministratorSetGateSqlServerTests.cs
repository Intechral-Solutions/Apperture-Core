using System.Data;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The global administrator-set gate against real SQL Server (IAM-10 gate P-8; A10.2b.13, A10.2b.15): acquisition and release,
/// the bounded wait, the typed contention results, cancellation, deadlock, and serialization of writers on separate connections.
/// Every case runs on a catalog with the default locking READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT: the gate is an
/// application lock, which does not depend on either. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PlatformAdministratorSetGateSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private readonly AuditIsolationCatalogs _catalogs;

    public PlatformAdministratorSetGateSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    /// <summary>Takes a lock on <paramref name="resource"/> in the context's transaction, as raw SQL; returns the return code.</summary>
    internal static async Task<int> GetAppLockAsync(AppertureDbContext db, string resource, int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @t; SELECT @r;";
        command.Parameters.Add(new SqlParameter("@res", resource));
        command.Parameters.Add(new SqlParameter("@t", timeoutMilliseconds));
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    internal static async Task ExecuteAsync(AppertureDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Waits until the server lists exactly <paramref name="count"/> requests waiting for an application lock in this catalog.</summary>
    internal static async Task WaitForWaitersAsync(AuditSqlScratch scratch, int count, params Task[] mustStillBeWaiting)
    {
        var deadline = DateTime.UtcNow.Add(Patience);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var task in mustStillBeWaiting)
            {
                if (task.IsCompleted)
                {
                    var detail = task.IsFaulted ? task.Exception!.GetBaseException().Message : "it completed";
                    throw new Xunit.Sdk.XunitException($"A writer did not wait for the gate ({detail}).");
                }
            }

            var waiting = await scratch.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID();");
            if (waiting == count)
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException($"Did not see {count} request(s) waiting for an application lock within {Patience.TotalSeconds} seconds.");
    }

    // ------------------------------------------------------------------ acquisition and release

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_second_holder_waits_for_the_first_and_both_commit_and_rollback_release_the_gate(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);

        await using var first = scratch.NewDb();
        await using var firstTx = await first.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(first).AcquireAsync();

        await using var second = scratch.NewDb();
        await using var secondTx = await second.Database.BeginTransactionAsync();
        var secondAcquired = Task.Run(async () => await new SqlPlatformAdministratorSetGate(second).AcquireAsync());
        await WaitForWaitersAsync(scratch, 1, secondAcquired);

        await firstTx.CommitAsync();
        await secondAcquired.WaitAsync(Patience);

        // The second holder released by rolling back: a third acquires at once.
        await secondTx.RollbackAsync();
        await using var third = scratch.NewDb();
        await using var thirdTx = await third.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(third, TimeSpan.FromMilliseconds(500)).AcquireAsync();
        await thirdTx.RollbackAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_wait_is_bounded_and_a_held_gate_past_the_bound_is_the_typed_busy_result(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await using var holder = scratch.NewDb();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(holder).AcquireAsync();

        await using var waiter = scratch.NewDb();
        await using var waiterTx = await waiter.Database.BeginTransactionAsync();
        var act = async () => await new SqlPlatformAdministratorSetGate(waiter, TimeSpan.FromMilliseconds(250)).AcquireAsync();

        var busy = (await act.Should().ThrowAsync<PlatformAdministratorSetBusyException>()).Which;
        busy.Outcome.Should().Be(PlatformAdministratorSetLockOutcome.TimedOut);
        busy.Code.Should().Be("governance_operation_busy");
        busy.Message.Should().NotContain("sp_getapplock");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Acquiring_outside_a_transaction_is_a_programming_error_not_a_silent_no_op(bool rcsi)
    {
        await using var db = _catalogs.For(rcsi).NewDb();

        var act = async () => await new SqlPlatformAdministratorSetGate(db).AcquireAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Cancelling_a_wait_is_the_callers_cancellation_not_contention(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await using var holder = scratch.NewDb();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(holder).AcquireAsync();

        await using var waiter = scratch.NewDb();
        await using var waiterTx = await waiter.Database.BeginTransactionAsync();
        using var cancel = new CancellationTokenSource();
        var waiting = Task.Run(async () => await new SqlPlatformAdministratorSetGate(waiter).AcquireAsync(cancel.Token));
        await WaitForWaitersAsync(scratch, 1, waiting);

        cancel.Cancel();

        var act = async () => await waiting;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_deadlock_victim_waiting_for_the_gate_is_the_typed_busy_result_not_a_failure(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        const string other = "apperture:test-deadlock-peer";

        // A holds the peer lock and then asks for the gate; B holds the gate and then asks for the peer lock. A is the chosen
        // victim (it has the lower deadlock priority), and the lock call reports that as a return code, not as error 1205.
        await using var a = scratch.NewDb();
        await using var aTx = await a.Database.BeginTransactionAsync();
        await ExecuteAsync(a, "SET DEADLOCK_PRIORITY LOW;");
        (await GetAppLockAsync(a, other, 0)).Should().BeGreaterThanOrEqualTo(0);

        await using var b = scratch.NewDb();
        await using var bTx = await b.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(b).AcquireAsync();

        var aAsksForGate = Task.Run(async () => await new SqlPlatformAdministratorSetGate(a, TimeSpan.FromSeconds(40)).AcquireAsync());
        await WaitForWaitersAsync(scratch, 1, aAsksForGate);
        var bAsksForPeer = Task.Run(async () => await GetAppLockAsync(b, other, 40_000));

        var act = async () => await aAsksForGate.WaitAsync(Patience);
        var busy = (await act.Should().ThrowAsync<PlatformAdministratorSetBusyException>()).Which;
        busy.Outcome.Should().Be(PlatformAdministratorSetLockOutcome.DeadlockVictim);

        // The victim's transaction ends; the other proceeds.
        await aTx.RollbackAsync();
        (await bAsksForPeer.WaitAsync(Patience)).Should().BeGreaterThanOrEqualTo(0);
        await bTx.RollbackAsync();
    }

    // ------------------------------------------------------------------ serialization

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Writers_on_separate_connections_run_one_at_a_time(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var inside = 0;
        var maxInside = 0;
        var completed = 0;

        async Task WriterAsync()
        {
            await using var db = scratch.NewDb();
            await using var tx = await db.Database.BeginTransactionAsync();
            await new SqlPlatformAdministratorSetGate(db).AcquireAsync();
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            await Task.Delay(30);
            Interlocked.Decrement(ref inside);
            await tx.CommitAsync();
            Interlocked.Increment(ref completed);
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(WriterAsync))).WaitAsync(Patience);

        completed.Should().Be(8);
        maxInside.Should().Be(1, "the gate admits one holder at a time, across connections");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_gate_is_database_scoped_so_it_does_not_serialize_other_catalogs(bool rcsi)
    {
        // The emergency command line is another process on the same database: the lock is shared there, and only there.
        var scratch = _catalogs.For(rcsi);
        var elsewhere = _catalogs.For(!rcsi);

        await using var holder = scratch.NewDb();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(holder).AcquireAsync();

        await using var other = elsewhere.NewDb();
        await using var otherTx = await other.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(other, TimeSpan.FromMilliseconds(500)).AcquireAsync();

        await using var sameCatalog = scratch.NewDb();
        await using var sameTx = await sameCatalog.Database.BeginTransactionAsync();
        var act = async () => await new SqlPlatformAdministratorSetGate(sameCatalog, TimeSpan.FromMilliseconds(250)).AcquireAsync();
        await act.Should().ThrowAsync<PlatformAdministratorSetBusyException>();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
                return;
        }
    }
}
