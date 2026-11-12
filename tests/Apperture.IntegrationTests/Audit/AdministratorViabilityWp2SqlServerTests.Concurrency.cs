using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP2: the global gate against real interleavings. Every case forces the overlap: the first writer is parked inside its
// unit, with everything it holds, until the second is shown to be waiting. The wait is shown to be on the application lock and on
// no row, so that disjoint row locks cannot have produced the result by accident. A control with the gate removed shows what the
// same interleaving does without it.
public sealed partial class AdministratorViabilityWp2SqlServerTests
{
    // ---------------------------------------------------------------------------------------------- the server's view

    private static Task<int> GrantedApplicationLocksAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'GRANT' AND resource_database_id = DB_ID();");

    private static Task<int> RowLocksHeldAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type IN (N'KEY', N'RID') AND request_status = N'GRANT' AND resource_database_id = DB_ID();");

    private static Task<int> RowLockWaitersAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type IN (N'KEY', N'RID', N'PAGE', N'OBJECT') AND request_status = N'WAIT' AND resource_database_id = DB_ID();");

    private static Task<int> ApplicationLockWaitersAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID();");

    /// <summary>Tries to take the gate from a session of its own without waiting: -1 means somebody holds it.</summary>
    private static async Task<int> ProbeGateAsync(AuditSqlScratch scratch)
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

    private static async Task WaitForRowLockWaiterAsync(AuditSqlScratch scratch, Task mustStillBeWaiting)
    {
        var deadline = DateTime.UtcNow.Add(Patience);
        while (DateTime.UtcNow < deadline)
        {
            if (mustStillBeWaiting.IsCompleted)
                throw new Xunit.Sdk.XunitException("The writer did not wait for a row lock; it completed.");
            if (await RowLockWaitersAsync(scratch) > 0)
                return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("No request was seen waiting for a row lock.");
    }

    // ------------------------------------------------------------------------------------------------ the pairs

    public static IEnumerable<object[]> PairsOnBothModes() =>
        from rcsi in new[] { false, true }
        from first in Enum.GetValues<Op>()
        from second in Enum.GetValues<Op>()
        select new object[] { first, second, rcsi };

    private sealed record Pair(RoleAuthorityWorld World, string X, string Y, string ActorA, string ActorB);

    /// <summary>
    /// Two targets, each viable only by the path its writer removes, and so that either change alone leaves the other target viable
    /// and both together leave nobody. Recovery Access is healthy (so only I-VIABLE can refuse) and each writer has an actor of its own
    /// who cannot sign in, so that the writers share no actor row.
    /// </summary>
    private async Task<Pair> PairAsync(bool rcsi, Op first, Op second)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        await HealthyRecoveryAsync(world);
        var actorA = await ActorAsync(world, "actor-a");
        var actorB = await ActorAsync(world, "actor-b");
        var x = await TargetOfAsync(world, first, "x");
        var y = await TargetOfAsync(world, second, "y");
        return new Pair(world, x, y, actorA, actorB);
    }

    private async Task AssertOneCommitAndOneRefusalAsync(Pair p, Op first, Op second, GovernanceConflictException refusal)
    {
        refusal.Code.Should().Be(RefusalCode(second));
        refusal.Message.Should().NotBeNullOrWhiteSpace();

        (await AppliedAsync(p.World, first, p.X)).Should().BeTrue("the first writer committed");
        (await AppliedAsync(p.World, second, p.Y)).Should().BeFalse("the second was refused, and its provisional change rolled back");
        (await p.World.AuditRowsAsync(SuccessAction(first), AuditTarget(first, p.X))).Should().Be(1);
        (await p.World.AuditRowsAsync(BlockedAction(first), AuditTarget(first, p.X))).Should().Be(0);
        (await p.World.AuditRowsAsync(BlockedAction(second), AuditTarget(second, p.Y))).Should().Be(1, "the refusal is recorded once, after the rollback");
        (await p.World.AuditRowsAsync(SuccessAction(second), AuditTarget(second, p.Y))).Should().Be(0);

        var state = await ViabilityAsync(p.World.Scratch, EntraReading);
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.Y], "an administrator who can sign in is left, in the running host");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.Y], "and after a restart");
    }

    [Theory]
    [MemberData(nameof(PairsOnBothModes))]
    public async Task A1_two_concurrent_reductions_that_together_leave_nobody_commit_once_and_refuse_once(Op first, Op second, bool rcsi)
    {
        var p = await PairAsync(rcsi, first, second);
        var scratch = p.World.Scratch;

        // The first writer runs everything but the commit: it holds the gate and its row locks and has read the state its change leaves.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var a = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await RunAsync(p.World, first, p.ActorA, p.X, db, EntraReading);
        });
        await pause.Reached.WaitAsync(Patience);
        (await ProbeGateAsync(scratch)).Should().Be(-1, "the first writer holds the administrator-set gate");
        (await GrantedApplicationLocksAsync(scratch)).Should().BeGreaterThanOrEqualTo(2, "its operation lock and the gate");
        (await RowLocksHeldAsync(scratch)).Should().BeGreaterThan(0, "it has already applied its change");

        // The second writer, on a different user and (in most pairs) a different kind of change, cannot get past the gate.
        await using var dbB = scratch.NewDb();
        var b = Task.Run(() => RunAsync(p.World, second, p.ActorB, p.Y, dbB, EntraReading));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(scratch, 1, b);
        (await RowLockWaitersAsync(scratch)).Should().Be(0, "the second writer waits for the application lock, not for a row: it has read and locked nothing");

        pause.Release();
        await a.WaitAsync(Patience);
        var refusal = await RefusedAsync(() => b.WaitAsync(Patience));

        await AssertOneCommitAndOneRefusalAsync(p, first, second, refusal);
    }

    [Theory]
    [MemberData(nameof(PairsOnBothModes))]
    public async Task A1_the_same_two_run_one_after_the_other_give_the_same_result(Op first, Op second, bool rcsi)
    {
        var p = await PairAsync(rcsi, first, second);

        await using (var db = p.World.Scratch.NewDb())
            await RunAsync(p.World, first, p.ActorA, p.X, db, EntraReading);
        await using var dbB = p.World.Scratch.NewDb();
        var refusal = await RefusedAsync(() => RunAsync(p.World, second, p.ActorB, p.Y, dbB, EntraReading));

        await AssertOneCommitAndOneRefusalAsync(p, first, second, refusal);
    }

    public static IEnumerable<object[]> ControlPairs() =>
        from rcsi in new[] { false, true }
        from pair in new[] { (Op.DisableCredential, Op.DisableCredential), (Op.RemoveRole, Op.RemoveLocalLink), (Op.RemoveEntraLink, Op.DisableCredential) }
        select new object[] { pair.Item1, pair.Item2, rcsi };

    [Theory]
    [MemberData(nameof(ControlPairs))]
    public async Task Control_with_the_gate_removed_the_same_interleaving_is_not_serialized_and_under_row_versioning_eliminates_the_last_administrator(Op first, Op second, bool rcsi)
    {
        var p = await PairAsync(rcsi, first, second);
        var scratch = p.World.Scratch;

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var a = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await RunAsync(p.World, first, p.ActorA, p.X, db, EntraReading, gate: new NoGate());
        });
        await pause.Reached.WaitAsync(Patience);
        (await ProbeGateAsync(scratch)).Should().BeGreaterThanOrEqualTo(0, "this writer never took the gate");

        await using var dbB = scratch.NewDb();
        var b = Task.Run(() => RunAsync(p.World, second, p.ActorB, p.Y, dbB, EntraReading, gate: new NoGate()));

        if (rcsi)
        {
            // Under row versioning nothing else stands in the second writer's way: it reads the committed past, decides, and commits.
            await b.WaitAsync(Patience);
            pause.Release();
            await a.WaitAsync(Patience);

            (await AppliedAsync(p.World, first, p.X)).Should().BeTrue();
            (await AppliedAsync(p.World, second, p.Y)).Should().BeTrue("both were allowed: this is the hole the gate closes");
            (await ViabilityAsync(p.World.Scratch, EntraReading)).Now.ViableOrdinaryAdministrators.Should().BeEmpty(
                "without the gate the two reductions together left no administrator who can sign in");
        }
        else
        {
            // Under locking READ COMMITTED the second writer's reads are held up by the first one's uncommitted rows: protection
            // by a row lock, which is incidental (it depends on what the writers happen to touch) and is not the gate.
            await WaitForRowLockWaiterAsync(scratch, b);
            (await ApplicationLockWaitersAsync(scratch)).Should().Be(0, "it waits on a row, not on the administrator-set gate");
            pause.Release();
            await a.WaitAsync(Patience);
            await RefusedAsync(() => b.WaitAsync(Patience));
        }
    }

    // ---------------------------------------------------------------------------- row-lock-only writers do not wait

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A13_a_writer_that_takes_only_row_locks_never_waits_for_the_gate(bool rcsi)
    {
        // The key-range locks of the authority snapshot also cover the next key of the index, so two writers whose user ids happen to be
        // neighbours would wait for each other by accident. The ids here are fixed so that they are not: the first writer's users sort
        // last, the second writer's first, with an unrelated assignment between them.
        var world = await FreshAsync(rcsi);
        var scratch = world.Scratch;
        await world.SetActiveRegistrationAsync(Tenant);
        async Task<string> UserAsync(string prefix, string stem, params string[] roles)
        {
            var id = prefix + Guid.NewGuid().ToString("N")[..22];
            await scratch.AddUserAsync(id, AuditRecorderTestSupport.NewUsername(stem));
            foreach (var role in roles)
                await world.AssignRawAsync(id, role);
            if (roles.Contains(Admin))
                await world.AddCredentialAsync(id);
            return id;
        }

        var assignable = await world.NewRoleAsync("assignable", P);
        var assignerRole = await world.NewRoleAsync("assigner-role", RoleAssign, P);
        var assigner = await UserAsync("10", "assigner", assignerRole);
        var user = await UserAsync("11", "user");
        _ = await UserAsync("80", "separator", assignable);
        var actorA = await UserAsync("f0", "actor-a", Admin);
        await DisableCredentialRawAsync(world, actorA);
        var targetX = await UserAsync("f1", "target-x", Admin);
        _ = await UserAsync("f2", "spare", Admin);
        var p = new Pair(world, targetX, string.Empty, actorA, string.Empty);

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var a = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await RunAsync(p.World, Op.RemoveRole, p.ActorA, p.X, db, EntraReading);
        });
        await pause.Reached.WaitAsync(Patience);
        (await ProbeGateAsync(scratch)).Should().Be(-1);

        // Assigning an ordinary role holds row locks of its own and needs no gate, so it is not held up by the gate's holder.
        await using (var db = scratch.NewDb())
            await Service(p.World, db, assigner).AssignRoleAsync(new UserId(user), new RoleId(assignable));

        (await p.World.AssignmentExistsAsync(user, assignable)).Should().BeTrue();
        (await ApplicationLockWaitersAsync(scratch)).Should().Be(0);

        pause.Release();
        await a.WaitAsync(Patience);
    }

    // ---------------------------------------------------------------------------------------------- lock order

    public enum UnitKind
    {
        RemoveRole,
        RemoveRoleSpelledInCapitals,
        RemoveRoleSpelledWithPadding,
        DisableCredential,
        RemoveLocalLink,
        RemoveEntraLink,
        AssignRole,
        AddLocalLink,
        EnableCredential,
    }

    public static IEnumerable<object[]> EveryUnitOnBothModes() =>
        from kind in Enum.GetValues<UnitKind>()
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    [Theory]
    [MemberData(nameof(EveryUnitOnBothModes))]
    public async Task A14_the_gate_is_the_first_lock_after_the_operation_lock_before_any_authority_row_lock_or_write(UnitKind kind, bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        await HealthyRecoveryAsync(world);
        await LocalAdminAsync(world, "spare");
        var actor = await ActorAsync(world, "actor");
        var log = new OrderedLog();

        await using var db = world.Scratch.NewDb(new LoggingStatements(log));
        var gate = new ProbingGate(new SqlPlatformAdministratorSetGate(db), log, db);
        var service = Service(world, db, actor, EntraReading, gate);

        switch (kind)
        {
            case UnitKind.RemoveRole:
            case UnitKind.RemoveRoleSpelledInCapitals:
            case UnitKind.RemoveRoleSpelledWithPadding:
            {
                var target = await world.NewUserAsync("target", Admin);
                var spelling = kind switch
                {
                    UnitKind.RemoveRoleSpelledInCapitals => Admin.ToUpperInvariant(),
                    UnitKind.RemoveRoleSpelledWithPadding => Admin + " ",
                    _ => Admin,
                };
                await service.RemoveRoleAsync(new UserId(target), new RoleId(spelling));
                (await world.AssignmentExistsAsync(target, Admin)).Should().BeFalse();
                break;
            }

            case UnitKind.DisableCredential:
            {
                var target = await world.NewUserAsync("target", Admin);
                await service.SetLocalCredentialDisabledAsync(new UserId(target), disabled: true);
                break;
            }

            case UnitKind.RemoveLocalLink:
            {
                var target = await TargetOfAsync(world, Op.RemoveLocalLink, "target");
                await service.RemoveIdentityLinkAsync(new UserId(target), IdentitySource.Local, await UsernameOfAsync(world, target));
                break;
            }

            case UnitKind.RemoveEntraLink:
            {
                var target = await TargetOfAsync(world, Op.RemoveEntraLink, "target");
                await service.RemoveIdentityLinkAsync(new UserId(target), IdentitySource.Entra, "oid-" + target);
                break;
            }

            case UnitKind.AssignRole:
            {
                var newcomer = await world.NewUserAsync("newcomer");
                await service.AssignRoleAsync(new UserId(newcomer), new RoleId(Admin));
                break;
            }

            case UnitKind.EnableCredential:
            {
                var stranded = await StrandedAdminAsync(world, "stranded-target");
                await service.SetLocalCredentialDisabledAsync(new UserId(stranded), disabled: false);
                break;
            }

            default:
            {
                var linkless = await world.NewUserAsync("linkless", Admin);
                await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{linkless}';");
                await service.AddIdentityLinkAsync(new UserId(linkless), IdentitySource.Local, null);
                break;
            }
        }

        var entries = log.Entries;
        var gateAt = entries.ToList().IndexOf(GateEntry);
        entries.Count(e => e == GateEntry).Should().Be(1, "one gate acquisition per attempt");
        gateAt.Should().BeGreaterThanOrEqualTo(0);

        gate.HeldBefore.Should().Be(1, "the operation lock is held when the gate is requested, and nothing else");
        gate.HeldAfter.Should().Be(2, "the gate is the second application lock");

        var beforeGate = entries.Take(gateAt).ToList();
        beforeGate.Should().NotContain(
            e => IsLockingOrWriting(e),
            "no authority row lock and no write may precede the gate, or two writers could wait for each other");
        entries.Skip(gateAt).Should().Contain(e => IsLockingOrWriting(e), "the unit does lock and write after the gate; the order is what is being checked");
        if (kind is UnitKind.RemoveRole or UnitKind.RemoveRoleSpelledInCapitals or UnitKind.RemoveRoleSpelledWithPadding or UnitKind.AssignRole)
            entries.Skip(gateAt).Should().Contain(e => e.Contains("UPDLOCK"), "the authority row locks follow the gate");

        if (kind is UnitKind.AssignRole or UnitKind.AddLocalLink or UnitKind.EnableCredential)
        {
            // A raise-only writer: its assertion reads the state after the change inside the audited transaction, before the audit row.
            var auditInsert = entries.ToList().FindIndex(e => e.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
            auditInsert.Should().BeGreaterThan(gateAt);
            var lastWrite = entries.ToList().FindLastIndex(
                auditInsert - 1,
                auditInsert,
                e => (e.Contains("INSERT INTO [", StringComparison.Ordinal) || e.Contains("UPDATE [", StringComparison.Ordinal) || e.Contains("DELETE FROM", StringComparison.Ordinal))
                     && !e.Contains("[AuditLogs]", StringComparison.Ordinal));
            lastWrite.Should().BeGreaterThan(gateAt);
            entries.Skip(lastWrite + 1).Take(auditInsert - lastWrite - 1)
                .Should().Contain(e => e.Contains("[UserRoles]", StringComparison.Ordinal) && e.Contains("SELECT", StringComparison.Ordinal),
                    "the state after the change is read in the same transaction, before the audit row");
        }
    }

    private static bool IsLockingOrWriting(string statement) =>
        statement.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("UPDATE [", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase)
        || statement.Contains("MERGE [", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A14_removing_a_role_other_than_the_administrator_role_never_takes_the_gate(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var role = await world.NewRoleAsync("ordinary", P);
        var assigner = await world.NewUserAsync("assigner", await world.NewRoleAsync("assigner-role", RoleAssign, P));
        var user = await world.NewUserAsync("user", role);
        var log = new OrderedLog();

        await using var db = world.Scratch.NewDb(new LoggingStatements(log));
        var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        await Service(world, db, assigner, gate: gate).RemoveRoleAsync(new UserId(user), new RoleId(role));

        gate.Acquired.Should().Be(0, "an ordinary role cannot change administrator viability, so the writer takes row locks only");
        (await world.AssignmentExistsAsync(user, role)).Should().BeFalse();
    }

    /// <summary>The real gate, noting the application locks the connection holds just before and just after it is taken.</summary>
    private sealed class ProbingGate(IPlatformAdministratorSetGate inner, OrderedLog log, AppertureDbContext db) : IPlatformAdministratorSetGate
    {
        public int HeldBefore { get; private set; }
        public int HeldAfter { get; private set; }

        public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
        {
            log.Add(GateEntry);
            HeldBefore = await HeldApplicationLocksAsync(cancellationToken);
            await inner.AcquireAsync(cancellationToken);
            HeldAfter = await HeldApplicationLocksAsync(cancellationToken);
        }

        private async Task<int> HeldApplicationLocksAsync(CancellationToken cancellationToken)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText =
                "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'GRANT' AND request_session_id = @@SPID;";
            return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
    }

    // ------------------------------------------------------------------------------ contention is not a refusal

    public static IEnumerable<object[]> EveryOpOnBothModes() =>
        from op in Enum.GetValues<Op>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    private async Task<(RoleAuthorityWorld World, string Actor, string Target)> AllowedChangeAsync(bool rcsi, Op op)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        await HealthyRecoveryAsync(world);
        await LocalAdminAsync(world, "spare");
        var actor = await ActorAsync(world, "actor");
        var target = await TargetOfAsync(world, op, "target");
        return (world, actor, target);
    }

    private static Task HoldGateAsync(RawSession session) =>
        session.ExecuteAsync(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName
            + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;");

    [Theory]
    [MemberData(nameof(EveryOpOnBothModes))]
    public async Task A15_a_gate_held_past_the_bound_is_governance_operation_busy_and_nothing_changes_then_the_retry_succeeds(Op op, bool rcsi)
    {
        var (world, actor, target) = await AllowedChangeAsync(rcsi, op);
        var before = await StateAsync(world.Scratch);

        await using (var holder = await RawSession.BeginAsync(world.Scratch.ConnectionString))
        {
            await HoldGateAsync(holder);
            await using var db = world.Scratch.NewDb();
            var impatient = new SqlPlatformAdministratorSetGate(db, TimeSpan.FromMilliseconds(300));

            var act = () => RunAsync(world, op, actor, target, db, EntraReading, impatient);

            var busy = (await act.Should().ThrowAsync<PlatformAdministratorSetBusyException>()).Which;
            busy.Code.Should().Be("governance_operation_busy");
            busy.Outcome.Should().Be(PlatformAdministratorSetLockOutcome.TimedOut);
            (await StateAsync(world.Scratch)).Should().Be(before);
            (await world.AuditRowsAsync(SuccessAction(op), AuditTarget(op, target))).Should().Be(0);
            (await world.AuditRowsAsync(BlockedAction(op), AuditTarget(op, target))).Should().Be(0, "contention is not a business refusal and writes no refusal row");
            await holder.RollbackAsync();
        }

        await using var retryDb = world.Scratch.NewDb();
        await RunAsync(world, op, actor, target, retryDb, EntraReading);
        (await AppliedAsync(world, op, target)).Should().BeTrue("the busy result was transient: the same request succeeds once the gate is free");
        (await world.AuditRowsAsync(SuccessAction(op), AuditTarget(op, target))).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(EveryOpOnBothModes))]
    public async Task A15_cancelling_the_wait_is_the_callers_cancellation_not_busy_and_not_a_refusal(Op op, bool rcsi)
    {
        var (world, actor, target) = await AllowedChangeAsync(rcsi, op);
        var before = await StateAsync(world.Scratch);

        await using var holder = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        await HoldGateAsync(holder);
        using var cancel = new CancellationTokenSource();
        await using var db = world.Scratch.NewDb();
        var waiting = Task.Run(() => RunAsync(world, op, actor, target, db, EntraReading, cancellationToken: cancel.Token));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(world.Scratch, 1, waiting);

        cancel.Cancel();

        await ((Func<Task>)(() => waiting)).Should().ThrowAsync<OperationCanceledException>();
        (await StateAsync(world.Scratch)).Should().Be(before);
        (await world.AuditRowsAsync(SuccessAction(op), AuditTarget(op, target))).Should().Be(0);
        (await world.AuditRowsAsync(BlockedAction(op), AuditTarget(op, target))).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------- a deadlock reruns and re-takes the gate

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A17_a_row_lock_deadlock_victim_reruns_the_whole_unit_takes_the_gate_again_and_writes_no_refusal(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var spare = await LocalAdminAsync(world, "spare");
        var actor = await LocalAdminAsync(world, "actor");
        var target = await LocalAdminAsync(world, "target");

        var deadlocks = new DeadlockLog();
        var pause = PauseAt.AfterActorAssignmentLock();
        var attempts = new AuditRecorderTestSupport.AttemptCounter();
        var log = new OrderedLog();
        var gates = new List<LoggingGate>();
        var operation = Task.Run(async () =>
        {
            await using var db = world.NewDeadlockAwareDb(deadlocks, pause, attempts);
            var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
            gates.Add(gate);
            await Service(world, db, actor, gate: gate).RemoveRoleAsync(new UserId(target), new RoleId(Admin));
        });
        await pause.Reached.WaitAsync(Patience);

        // A competing session that wins any deadlock: it holds the administrator role row and wants the assignments the operation holds.
        await using var competitor = await RawSession.BeginAsync(world.Scratch.ConnectionString, highDeadlockPriority: true);
        await competitor.ExecuteAsync($"UPDATE Roles SET Name = Name WHERE Id = N'{Admin}';");
        var competitorSecond = Task.Run(() => competitor.ExecuteAsync("UPDATE UserRoles SET RoleId = RoleId WHERE RoleId = @r;", ("@r", Admin)));
        await world.WaitUntilBlockedOnAsync("UserRoles", competitorSecond);

        pause.Release();
        var deadline = DateTime.UtcNow.Add(Patience);
        while (Volatile.Read(ref deadlocks.Victims) == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        deadlocks.Victims.Should().Be(1, "the server chose the operation as the deadlock victim");

        await competitorSecond.WaitAsync(Patience);
        await competitor.CommitAsync();
        await operation.WaitAsync(Patience);

        attempts.Started.Should().Be(2, "one attempt lost the deadlock and the strategy reran it");
        log.Entries.Count(e => e == GateEntry).Should().Be(2, "a rerun is a new transaction: the gate is taken again and the state is read again");
        gates.Should().ContainSingle();
        (await world.AssignmentExistsAsync(target, Admin)).Should().BeFalse();
        (await world.AuditRowsAsync(AuditActionNames.RoleRemoved, target)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastPlatformAdminRemoval, target)).Should().Be(0, "a deadlock is a transient failure, never a refusal");
        (await ViabilityAsync(world.Scratch)).Now.ViableOrdinaryAdministrators.Should().Contain(spare);
    }
}
