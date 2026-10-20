using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2b WP2, A10.2b.17 (M6): each audited unit that now takes the administrator-set gate keeps the recorder's guarantees. An
// audit-insert failure leaves nothing; a lost acknowledgement is verified, not rerun; a confirmed rollback reruns the whole unit,
// which takes the gate again and decides again; an unknown outcome is wholly committed or wholly absent; an operation lock that is
// not granted changes nothing. A refusal produced after the write is rolled back and recorded, and a failure to record it does not
// change what the caller sees.
public sealed partial class AdministratorViabilityWp2SqlServerTests
{
    public enum M6Unit
    {
        RemoveRole,
        DisableCredential,
        RemoveLocalLink,
        AssignRole,
        AddLocalLink,
    }

    public static IEnumerable<object[]> EveryM6UnitOnBothModes() =>
        from unit in Enum.GetValues<M6Unit>()
        from rcsi in new[] { false, true }
        select new object[] { unit, rcsi };

    private sealed class M6Case
    {
        public required string Actor { get; init; }
        public required RoleAuthorityWorld World { get; init; }
        public required Func<AppertureDbContext, IAuditedUnitOfWork, IPlatformAdministratorSetGate?, Task> Run { get; init; }
        public required Func<Task<bool>> ChangeLanded { get; init; }
        public required Func<Task<int>> RecordedRows { get; init; }
    }

    /// <summary>One change that is allowed (another administrator can always sign in), ready to run on a context and unit it is given.</summary>
    private async Task<M6Case> ArrangeM6Async(bool rcsi, M6Unit unit)
    {
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        await HealthyRecoveryAsync(world);
        await LocalAdminAsync(world, "spare");
        var actor = await ActorAsync(world, "actor");

        switch (unit)
        {
            case M6Unit.RemoveRole:
            {
                var target = await world.NewUserAsync("target", Admin);
                return new M6Case
                {
                    Actor = actor,
                    World = world,
                    Run = (db, u, gate) => Service(world, db, actor, EntraReading, gate, u).RemoveRoleAsync(new UserId(target), new RoleId(Admin)).AsTask(),
                    ChangeLanded = async () => !await world.AssignmentExistsAsync(target, Admin),
                    RecordedRows = () => world.AuditRowsAsync(AuditActionNames.RoleRemoved, target),
                };
            }

            case M6Unit.DisableCredential:
            {
                var target = await world.NewUserAsync("target", Admin);
                return new M6Case
                {
                    Actor = actor,
                    World = world,
                    Run = (db, u, gate) => Service(world, db, actor, EntraReading, gate, u).SetLocalCredentialDisabledAsync(new UserId(target), disabled: true).AsTask(),
                    ChangeLanded = () => world.CredentialDisabledAsync(target),
                    RecordedRows = () => world.AuditRowsAsync(AuditActionNames.CredentialDisabled, target),
                };
            }

            case M6Unit.RemoveLocalLink:
            {
                var target = await TargetOfAsync(world, Op.RemoveLocalLink, "target");
                var username = await UsernameOfAsync(world, target);
                return new M6Case
                {
                    Actor = actor,
                    World = world,
                    Run = (db, u, gate) => Service(world, db, actor, EntraReading, gate, u).RemoveIdentityLinkAsync(new UserId(target), IdentitySource.Local, username).AsTask(),
                    ChangeLanded = async () => await world.LinkCountAsync(target, 1) == 0,
                    RecordedRows = () => world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "L" + target),
                };
            }

            case M6Unit.AssignRole:
            {
                var newcomer = await world.NewUserAsync("newcomer");
                return new M6Case
                {
                    Actor = actor,
                    World = world,
                    Run = (db, u, gate) => Service(world, db, actor, EntraReading, gate, u).AssignRoleAsync(new UserId(newcomer), new RoleId(Admin)).AsTask(),
                    ChangeLanded = () => world.AssignmentExistsAsync(newcomer, Admin),
                    RecordedRows = () => world.AuditRowsAsync(AuditActionNames.RoleAssigned, newcomer),
                };
            }

            default:
            {
                var linkless = await world.NewUserAsync("linkless", Admin);
                await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{linkless}';");
                return new M6Case
                {
                    Actor = actor,
                    World = world,
                    Run = (db, u, gate) => Service(world, db, actor, EntraReading, gate, u).AddIdentityLinkAsync(new UserId(linkless), IdentitySource.Local, null).AsTask(),
                    ChangeLanded = async () => await world.LinkCountAsync(linkless, 1) == 1,
                    RecordedRows = () => world.Scratch.ScalarAsync<int>(
                        $"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.IdentityLinkAdded}' AND Details LIKE N'%{linkless}%';"),
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_an_audit_insert_failure_leaves_nothing_changed_and_nothing_recorded(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var before = await StateAsync(c.World.Scratch);

        await using var db = c.World.Scratch.NewDb(failAudit);
        var act = () => c.Run(db, NewUnit(db, new Principal(c.Actor), FastOptions()), null);

        await act.Should().ThrowAsync<Exception>();
        failAudit.Failures.Should().BeGreaterThan(0);
        (await c.ChangeLanded()).Should().BeFalse("the change and its audit row commit together, or not at all");
        (await c.RecordedRows()).Should().Be(0);
        (await StateAsync(c.World.Scratch)).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_a_lost_acknowledgement_is_verified_as_committed_and_not_rerun(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var attempts = new AttemptCounter();

        await using var db = c.World.Scratch.NewDb(new LostCommitAcknowledgement(), attempts);
        var log = new OrderedLog();
        var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        await c.Run(db, NewUnit(db, new Principal(c.Actor), FastOptions()), gate);

        attempts.Started.Should().Be(1, "verification confirmed the commit, so the decision is not taken again");
        gate.Acquired.Should().Be(1);
        (await c.ChangeLanded()).Should().BeTrue();
        (await c.RecordedRows()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_a_confirmed_rollback_reruns_the_unit_which_takes_the_gate_and_decides_again(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var attempts = new AttemptCounter();

        await using var db = c.World.Scratch.NewDb(new RolledBackCommit(), attempts);
        var log = new OrderedLog();
        var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        await c.Run(db, NewUnit(db, new Principal(c.Actor), FastOptions()), gate);

        attempts.Started.Should().Be(2, "verification confirmed the rollback, so one rerun was allowed");
        gate.Acquired.Should().Be(2, "a rerun is a new transaction: the gate it held was released with the rollback and is taken again");
        (await c.ChangeLanded()).Should().BeTrue();
        (await c.RecordedRows()).Should().Be(1, "the rolled-back attempt left no row");
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_an_unknown_outcome_is_wholly_committed_or_absent_never_half(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);

        await using var db = c.World.Scratch.NewDb(new LostCommitAcknowledgement());
        var act = () => c.Run(db, NewUnit(db, new Principal(c.Actor), FastOptions(), verification: new UnreachableVerification()), null);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        // The commit really happened here, so both landed; the point is that they are never one without the other.
        ((await c.ChangeLanded()), (await c.RecordedRows())).Should().Be((true, 1));
    }

    [Theory]
    [MemberData(nameof(EveryM6UnitOnBothModes))]
    public async Task M6_an_operation_lock_that_is_not_granted_changes_nothing_and_never_reaches_the_gate(M6Unit unit, bool rcsi)
    {
        var c = await ArrangeM6Async(rcsi, unit);
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(c.World.Scratch.ConnectionString, "apperture.audit-op." + operationId);
        var before = await StateAsync(c.World.Scratch);

        await using var db = c.World.Scratch.NewDb();
        var log = new OrderedLog();
        var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        var act = () => c.Run(db, NewUnit(db, new Principal(c.Actor), FastOptions(), eventIds: new FixedEventIds(operationId)), gate);

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        gate.Acquired.Should().Be(0, "the operation lock comes first: without it the unit does not go on to the gate");
        (await StateAsync(c.World.Scratch)).Should().Be(before);
        (await c.RecordedRows()).Should().Be(0);
    }

    // ---- a refusal produced after the write ----

    public enum RefusedUnit
    {
        RemoveRole,
        DisableCredential,
        RemoveLocalLink,
        RemoveEntraLink,
    }

    public static IEnumerable<object[]> EveryRefusedUnitOnBothModes() =>
        from unit in Enum.GetValues<RefusedUnit>()
        from rcsi in new[] { false, true }
        select new object[] { unit, rcsi };

    private static Op OpOf(RefusedUnit unit) => unit switch
    {
        RefusedUnit.RemoveRole => Op.RemoveRole,
        RefusedUnit.DisableCredential => Op.DisableCredential,
        RefusedUnit.RemoveLocalLink => Op.RemoveLocalLink,
        _ => Op.RemoveEntraLink,
    };

    private sealed class UnwritableEvents : IAuditEventWriter
    {
        public int Attempts { get; private set; }

        public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected: the audit store is unavailable.");

        public Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromResult(false);
        }
    }

    [Theory]
    [MemberData(nameof(EveryRefusedUnitOnBothModes))]
    public async Task M6_a_refusal_made_after_the_write_rolls_back_and_a_failing_refusal_row_does_not_change_what_the_caller_sees(RefusedUnit unit, bool rcsi)
    {
        var op = OpOf(unit);
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        await HealthyRecoveryAsync(world);
        var actor = await ActorAsync(world, "actor");
        var target = await TargetOfAsync(world, op, "target");
        var before = await StateAsync(world.Scratch);
        var events = new UnwritableEvents();

        await using var db = world.Scratch.NewDb();
        var act = () => RunAsync(world, op, actor, target, db, EntraReading, events: events);

        (await RefusedAsync(act)).Code.Should().Be(RefusalCode(op), "the caller sees the typed refusal, not the failure to record it");
        events.Attempts.Should().Be(1, "the refusal row was attempted once, after the rollback");
        (await StateAsync(world.Scratch)).Should().Be(before, "the provisional change was rolled back");
        (await AppliedAsync(world, op, target)).Should().BeFalse();
        (await world.AuditRowsAsync(SuccessAction(op), AuditTarget(op, target))).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(EveryRefusedUnitOnBothModes))]
    public async Task A17_a_refusal_is_recorded_once_with_the_operations_existing_action_and_changes_nothing_else(RefusedUnit unit, bool rcsi)
    {
        var op = OpOf(unit);
        var world = await FreshAsync(rcsi);
        await world.SetActiveRegistrationAsync(Tenant);
        await HealthyRecoveryAsync(world);
        var actor = await ActorAsync(world, "actor");
        var target = await TargetOfAsync(world, op, "target");
        var before = await StateAsync(world.Scratch);
        var auditBefore = await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using var db = world.Scratch.NewDb();
        await RefusedAsync(() => RunAsync(world, op, actor, target, db, EntraReading));

        (await StateAsync(world.Scratch)).Should().Be(before);
        (await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore + 1, "exactly one row: the refusal");
        var rows = await world.AuditRowDetailsAsync(BlockedAction(op), AuditTarget(op, target));
        rows.Should().ContainSingle();
        rows[0]["Outcome"].Should().NotBeNull();
    }
}
