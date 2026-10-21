using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP3: the registration writers against real interleavings with the person-driven administrator writers of WP2. Every case
// forces the overlap: the first writer is parked inside its unit, with everything it holds, until the second is shown to be waiting;
// the wait is shown to be on the application lock and on no row. A control with the gate removed shows what the same interleaving
// does without it. Contention is a busy outcome, never a refusal; a cancelled wait is the caller's cancellation.
public sealed partial class EntraRegistrationViabilityWp3SqlServerTests
{
    private sealed class AllowingStrongAuthentication : IStrongAuthenticationGate
    {
        public ValueTask<AuthenticationAssurance> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuthenticationAssurance("test", true, DateTimeOffset.UtcNow, "c1", true));

        public ValueTask RequireRecentStrongAuthenticationAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------- the server's view

    private static Task<int> RowLockWaitersAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type IN (N'KEY', N'RID', N'PAGE', N'OBJECT') AND request_status = N'WAIT' AND resource_database_id = DB_ID();");

    private static Task<int> ApplicationLockWaitersAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID();");

    private static Task<int> GrantedApplicationLocksAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'GRANT' AND resource_database_id = DB_ID();");

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

    // ---------------------------------------------------------------------------------------- the person-driven writers

    public enum Wp2Op
    {
        RemoveRole,
        DisableCredential,
        RemoveLocalLink,
    }

    private static string Wp2Code(Wp2Op op) => op switch
    {
        Wp2Op.RemoveRole => "last_viable_administrator",
        Wp2Op.DisableCredential => "last_local_credential",
        _ => "last_viable_sign_in_method",
    };

    private sealed record Pair(Scenario S, string B, string Wp2Actor);

    /// <summary>
    /// <c>A</c> signs in only through the active registration; <c>B</c> signs in only locally. A registration change that stops
    /// <c>A</c> leaves <c>B</c>, a change to <c>B</c> leaves <c>A</c>, and both together leave nobody.
    /// </summary>
    private async Task<Pair> PairAsync(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var b = await LocalAdminAsync(s.World, "b");
        await s.World.AddEntraLinkAsync(b, Tenant, proven: false);

        // The administrator writers' actor holds the role (removing it takes authority over it) but cannot sign in, so it counts for nothing.
        var actor = await s.World.NewUserAsync("wp2-actor", Admin);
        await s.World.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{actor}';");
        return new Pair(s, b, actor);
    }

    private static async Task RunWp2Async(Pair p, Wp2Op op, AppertureDbContext db, IPlatformAdministratorSetGate? gate = null)
    {
        var service = p.S.World.Service(
            db, p.Wp2Actor, advisoryAllowsAll: true, gate: gate, deployment: p.S.Reading, strongAuthentication: new AllowingStrongAuthentication());
        switch (op)
        {
            case Wp2Op.RemoveRole:
                await service.RemoveRoleAsync(new UserId(p.B), new RoleId(Admin));
                break;
            case Wp2Op.DisableCredential:
                await service.SetLocalCredentialDisabledAsync(new UserId(p.B), disabled: true);
                break;
            default:
                await service.RemoveIdentityLinkAsync(
                    new UserId(p.B), IdentitySource.Local, await p.S.World.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{p.B}';"));
                break;
        }
    }

    private static async Task<bool> Wp2AppliedAsync(Pair p, Wp2Op op) => op switch
    {
        Wp2Op.RemoveRole => !await p.S.World.AssignmentExistsAsync(p.B, Admin),
        Wp2Op.DisableCredential => await p.S.World.CredentialDisabledAsync(p.B),
        _ => await p.S.World.LinkCountAsync(p.B, 1) == 0,
    };

    private static async Task<bool> RegistrationAppliedAsync(Scenario s, RegOp op) => op switch
    {
        RegOp.Disable => !await s.Scratch.ScalarAsync<bool>($"SELECT Enabled FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';"),
        RegOp.Delete => await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';") == 0,
        RegOp.Switch => await s.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{s.R2}';"),
        _ => await s.Scratch.ScalarAsync<string>($"SELECT AzureTenantId FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';") == OtherTenant,
    };

    public static IEnumerable<object[]> PairsOnBothModes() =>
        from rcsi in new[] { false, true }
        from reg in Enum.GetValues<RegOp>()
        from wp2 in Enum.GetValues<Wp2Op>()
        select new object[] { reg, wp2, rcsi };

    private async Task AssertRegistrationCommittedAndAdministratorChangeRefusedAsync(Pair p, RegOp reg, Wp2Op wp2, GovernanceConflictException refusal)
    {
        refusal.Code.Should().Be(Wp2Code(wp2));
        (await RegistrationAppliedAsync(p.S, reg)).Should().BeTrue("the registration change committed first");
        (await Wp2AppliedAsync(p, wp2)).Should().BeFalse("the administrator change was refused after the registration change and rolled back");
        (await RowsAsync(p.S.Scratch, ActionOf(reg), TargetOf(p.S, reg))).Should().Be(1);
        (await RefusalRowsAsync(p.S.Scratch, TargetOf(p.S, reg))).Should().Be(0);
        var state = await ViabilityAsync(p.S.Scratch);
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.B], "an administrator who can sign in is left");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.B]);
    }

    [Theory]
    [MemberData(nameof(PairsOnBothModes))]
    public async Task A1_a_registration_change_then_an_administrator_change_that_together_leave_nobody_commit_once_and_refuse_once(RegOp reg, Wp2Op wp2, bool rcsi)
    {
        var p = await PairAsync(rcsi);
        var scratch = p.S.Scratch;

        // The registration writer runs everything but the commit: it holds the gate and its row locks and has read the state its change leaves.
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            var response = await RunAsync(p.S, reg, db);
            Succeeded(response).Should().BeTrue($"the first writer is allowed on its own: {CodeOf(response)}");
        });
        await pause.Reached.WaitAsync(Patience);
        (await ProbeGateAsync(scratch)).Should().Be(-1, "the registration writer holds the administrator-set gate");
        (await GrantedApplicationLocksAsync(scratch)).Should().BeGreaterThanOrEqualTo(2, "its operation lock and the gate");

        await using var dbB = scratch.NewDb();
        var second = Task.Run(() => RunWp2Async(p, wp2, dbB));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(scratch, 1, second);
        (await RowLockWaitersAsync(scratch)).Should().Be(0, "the second writer waits for the application lock, not for a row: it has read and locked nothing");

        pause.Release();
        await first.WaitAsync(Patience);
        var refusal = (await ((Func<Task>)(() => second.WaitAsync(Patience))).Should().ThrowAsync<GovernanceConflictException>()).Which;

        await AssertRegistrationCommittedAndAdministratorChangeRefusedAsync(p, reg, wp2, refusal);
    }

    [Theory]
    [MemberData(nameof(PairsOnBothModes))]
    public async Task A1_an_administrator_change_then_a_registration_change_that_together_leave_nobody_commit_once_and_refuse_once(RegOp reg, Wp2Op wp2, bool rcsi)
    {
        var p = await PairAsync(rcsi);
        var scratch = p.S.Scratch;

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await RunWp2Async(p, wp2, db);
        });
        await pause.Reached.WaitAsync(Patience);
        (await ProbeGateAsync(scratch)).Should().Be(-1);

        await using var dbR = scratch.NewDb();
        var second = Task.Run(() => RunAsync(p.S, reg, dbR));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(scratch, 1, second);
        (await RowLockWaitersAsync(scratch)).Should().Be(0, "the registration writer waits for the application lock, not for a row");

        pause.Release();
        await first.WaitAsync(Patience);
        var response = await second.WaitAsync(Patience);

        response.Should().BeOfType<ConflictObjectResult>();
        CodeOf(response).Should().Be("last_viable_sign_in_method");
        (await Wp2AppliedAsync(p, wp2)).Should().BeTrue("the administrator change committed first");
        (await RegistrationAppliedAsync(p.S, reg)).Should().BeFalse("the registration change was refused after it and rolled back");
        (await RefusalDetailsAsync(p.S.Scratch, TargetOf(p.S, reg))).Should().Equal(ChangeOf(reg) + "/sign_in_lockout");
        var state = await ViabilityAsync(p.S.Scratch);
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([p.S.A], "the administrator who signs in through Microsoft is left");
    }

    [Theory]
    [MemberData(nameof(PairsOnBothModes))]
    public async Task A1_the_same_two_run_one_after_the_other_give_the_same_result(RegOp reg, Wp2Op wp2, bool rcsi)
    {
        var p = await PairAsync(rcsi);

        await using (var db = p.S.Scratch.NewDb())
            Succeeded(await RunAsync(p.S, reg, db)).Should().BeTrue();
        await using var dbB = p.S.Scratch.NewDb();
        var refusal = (await ((Func<Task>)(() => RunWp2Async(p, wp2, dbB))).Should().ThrowAsync<GovernanceConflictException>()).Which;

        await AssertRegistrationCommittedAndAdministratorChangeRefusedAsync(p, reg, wp2, refusal);
    }

    public static IEnumerable<object[]> ControlPairs() =>
        from rcsi in new[] { false, true }
        from pair in new[] { (RegOp.Delete, Wp2Op.DisableCredential), (RegOp.Disable, Wp2Op.RemoveRole), (RegOp.Switch, Wp2Op.RemoveLocalLink) }
        select new object[] { pair.Item1, pair.Item2, rcsi };

    [Theory]
    [MemberData(nameof(ControlPairs))]
    public async Task Control_with_the_gate_removed_the_same_interleaving_is_not_serialized_and_under_row_versioning_leaves_nobody(RegOp reg, Wp2Op wp2, bool rcsi)
    {
        var p = await PairAsync(rcsi);
        var scratch = p.S.Scratch;

        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await RunAsync(p.S, reg, db, gate: new AdministratorViabilityWp2SqlServerTests.NoGate());
        });
        await pause.Reached.WaitAsync(Patience);
        (await ProbeGateAsync(scratch)).Should().BeGreaterThanOrEqualTo(0, "this writer never took the gate");

        await using var dbB = scratch.NewDb();
        var second = Task.Run(() => RunWp2Async(p, wp2, dbB, new AdministratorViabilityWp2SqlServerTests.NoGate()));

        if (rcsi)
        {
            // Under row versioning nothing else stands in the second writer's way: it reads the committed past, decides, and commits.
            await second.WaitAsync(Patience);
            pause.Release();
            await first.WaitAsync(Patience);

            (await RegistrationAppliedAsync(p.S, reg)).Should().BeTrue();
            (await Wp2AppliedAsync(p, wp2)).Should().BeTrue("both were allowed: this is the hole the gate closes");
            (await ViabilityAsync(scratch)).Now.ViableOrdinaryAdministrators.Should().BeEmpty("without the gate the two changes together left nobody able to sign in");
        }
        else
        {
            // Under locking READ COMMITTED the second writer's reads are held up by the first one's uncommitted rows: protection by a
            // row lock, which is incidental (it depends on what the writers happen to touch) and is not the gate.
            var deadline = DateTime.UtcNow.Add(Patience);
            while (await RowLockWaitersAsync(scratch) == 0)
            {
                second.IsCompleted.Should().BeFalse("the second writer must wait on a row");
                DateTime.UtcNow.Should().BeBefore(deadline);
                await Task.Delay(25);
            }

            (await ApplicationLockWaitersAsync(scratch)).Should().Be(0, "it waits on a row, not on the administrator-set gate");
            pause.Release();
            await first.WaitAsync(Patience);
            await ((Func<Task>)(() => second.WaitAsync(Patience))).Should().ThrowAsync<GovernanceConflictException>();
        }
    }

    // ----------------------------------------------------- the accepted tenant cannot move between another writer's two reads

    /// <summary>Arms once a statement satisfying <c>after</c> has run (the writer's own change), then pauses before the next one satisfying <c>pauseAt</c> (the start of the state read after it).</summary>
    private sealed class PauseBetweenReads(Func<string, bool> after, Func<string, bool> pauseAt) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _seen;

        public Task Reached => _reached.Task;

        public void Release() => _release.TrySetResult();

        private async Task PauseIfAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var text = command.CommandText;
            if (Volatile.Read(ref _seen) == 0)
            {
                if (after(text))
                    Volatile.Write(ref _seen, 1);
                return;
            }

            if (!pauseAt(text) || Interlocked.Exchange(ref _seen, 2) == 2)
                return;
            _reached.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            await PauseIfAsync(command, cancellationToken);
            return result;
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A1_a_registration_change_cannot_move_the_accepted_tenant_between_an_administrator_writers_state_before_and_after(bool rcsi)
    {
        var p = await PairAsync(rcsi);
        var scratch = p.S.Scratch;

        // The administrator writer disables B's credential and is parked after its change, before it reads the state after it.
        var pause = new PauseBetweenReads(
            after: sql => sql.Contains("UPDATE [LocalCredentials]", StringComparison.Ordinal),
            pauseAt: sql => sql.Contains("[EntraInstanceRegistrations]", StringComparison.Ordinal));
        var administrator = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await RunWp2Async(p, Wp2Op.DisableCredential, db);
        });
        await pause.Reached.WaitAsync(Patience);

        // A registration change that would move the accepted tenant starts now: it must wait for the gate, not run between the reads.
        await using var dbR = scratch.NewDb();
        var registration = Task.Run(() => RunAsync(p.S, RegOp.ChangeTenant, dbR));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(scratch, 1, registration);
        registration.IsCompleted.Should().BeFalse();
        (await scratch.ScalarAsync<string>($"SELECT AzureTenantId FROM EntraInstanceRegistrations WHERE Id = N'{p.S.R1}';")).Should().Be(Tenant, "the accepted tenant did not move between the administrator writer's reads");

        pause.Release();
        await administrator.WaitAsync(Patience);
        var response = await registration.WaitAsync(Patience);

        // Serial order: the credential change committed (A is left), then the tenant change would leave nobody, and is refused.
        (await p.S.World.CredentialDisabledAsync(p.B)).Should().BeTrue();
        CodeOf(response).Should().Be("last_viable_sign_in_method");
        (await scratch.ScalarAsync<string>($"SELECT AzureTenantId FROM EntraInstanceRegistrations WHERE Id = N'{p.S.R1}';")).Should().Be(Tenant);
    }

    // ----------------------------------------------------------------- row-lock-only writers never wait for the gate

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A13_a_registration_probe_that_writes_only_the_diagnostics_row_never_waits_for_the_gate(bool rcsi)
    {
        var p = await PairAsync(rcsi);
        var scratch = p.S.Scratch;
        var pause = PauseAt.BeforeStatement("INSERT INTO [AuditLogs]");
        var first = Task.Run(async () =>
        {
            await using var db = scratch.NewDb(pause);
            await RunAsync(p.S, RegOp.Delete, db);
        });
        await pause.Reached.WaitAsync(Patience);

        // A probe records its outcome on the registration's diagnostics row alone: telemetry, not a viability input. Another
        // registration's probe does not wait for the gate (it takes none); it is not the writer under test, so it runs to completion.
        await using var dbProbe = scratch.NewDb();
        var probe = new SqlEntraInstanceRegistrationRepository(dbProbe);
        await probe.UpdateOpenIdValidationOutcomeAsync(p.S.R2!, Apperture.Core.Application.Services.OpenIdConfigurationStatus.Valid, DateTimeOffset.UtcNow, "ok", null);

        (await ApplicationLockWaitersAsync(scratch)).Should().Be(0);
        pause.Release();
        await first.WaitAsync(Patience);
    }

    // ----------------------------------------------------------------------------- contention is not a refusal

    public static IEnumerable<object[]> EveryRegOpOnBothModes() =>
        from op in Enum.GetValues<RegOp>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    private static Task HoldGateAsync(RawSession session) =>
        session.ExecuteAsync(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName
            + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;");

    [Theory]
    [MemberData(nameof(EveryRegOpOnBothModes))]
    public async Task A15_a_gate_held_past_the_bound_is_governance_operation_busy_with_retry_and_nothing_changes_then_the_retry_succeeds(RegOp op, bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var before = await StateAsync(s.Scratch);
        var target = TargetOf(s, op);

        await using (var holder = await RawSession.BeginAsync(s.Scratch.ConnectionString))
        {
            await HoldGateAsync(holder);
            await using var db = s.Scratch.NewDb();
            var busy = await RunAsync(s, op, db, new SqlPlatformAdministratorSetGate(db, TimeSpan.FromMilliseconds(300)));

            busy.Should().BeOfType<ConflictObjectResult>();
            CodeOf(busy).Should().Be("governance_operation_busy");
            ((ObjectResult)busy).Value!.ToString().Should().Contain("retry = True");
            (await StateAsync(s.Scratch)).Should().Be(before);
            (await RowsAsync(s.Scratch, ActionOf(op), target)).Should().Be(0);
            (await RefusalRowsAsync(s.Scratch, target)).Should().Be(0, "contention is not a business refusal and writes no refusal row");
            await holder.RollbackAsync();
        }

        await using var retryDb = s.Scratch.NewDb();
        Succeeded(await RunAsync(s, op, retryDb)).Should().BeTrue("the busy result was transient: the same request succeeds once the gate is free");
        (await RowsAsync(s.Scratch, ActionOf(op), target)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(EveryRegOpOnBothModes))]
    public async Task A15_cancelling_the_wait_is_the_callers_cancellation_not_busy_and_not_a_refusal(RegOp op, bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var before = await StateAsync(s.Scratch);

        await using var holder = await RawSession.BeginAsync(s.Scratch.ConnectionString);
        await HoldGateAsync(holder);
        using var cancel = new CancellationTokenSource();
        await using var db = s.Scratch.NewDb();
        var waiting = Task.Run(() => RunAsync(s, op, db, cancellationToken: cancel.Token));
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(s.Scratch, 1, waiting);

        cancel.Cancel();

        await ((Func<Task>)(() => waiting)).Should().ThrowAsync<OperationCanceledException>();
        (await StateAsync(s.Scratch)).Should().Be(before);
        (await RefusalRowsAsync(s.Scratch, TargetOf(s, op))).Should().Be(0);
        await holder.RollbackAsync();
    }
}
