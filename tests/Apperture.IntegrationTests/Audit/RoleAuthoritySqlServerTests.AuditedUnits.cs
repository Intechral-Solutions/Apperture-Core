using Apperture.Core.Application.Audit;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2a, A10.2.12 (M6): each audited unit that changed (role create, permission replacement, assignment, removal) keeps the
// recorder's guarantees now that its decision is taken under the authority lock set: an audit-insert failure leaves nothing; a lost
// acknowledgement is verified, not rerun; a confirmed rollback reruns once; an unknown outcome is wholly committed or absent; and an
// operation lock that is not granted changes nothing.
public sealed partial class RoleAuthoritySqlServerTests
{
    public enum UnitKind
    {
        CreateRole,
        ReplacePermissions,
        AssignRole,
        RemoveRole,
    }

    public static IEnumerable<object[]> EveryUnitOnBothModes() =>
        from kind in Enum.GetValues<UnitKind>()
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    /// <summary>One operation, ready to run: it runs on a context it is given, and says whether its change landed and how many rows it recorded.</summary>
    private sealed class UnitCase
    {
        public required string Actor { get; init; }
        public required Func<RoleAuthorityWorld, AppertureDbContext, IAuditedUnitOfWork, Task> Run { get; init; }
        public required Func<Task<bool>> ChangeLanded { get; init; }
        public required Func<Task<int>> RecordedRows { get; init; }
    }

    private async Task<UnitCase> ArrangeAsync(RoleAuthorityWorld world, UnitKind kind)
    {
        switch (kind)
        {
            case UnitKind.CreateRole:
            {
                var actor = await world.NewUserAsync("creator", await world.NewRoleAsync("creator", RoleManage, PermissionAssign, P));
                var name = "Created " + Guid.NewGuid().ToString("N")[..8];
                return new UnitCase
                {
                    Actor = actor,
                    Run = (w, db, unit) => w.Service(db, actor, advisoryAllowsAll: true, unit: unit).CreateRoleAsync(name, null, [P]).AsTask(),
                    ChangeLanded = async () => await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Roles WHERE Name = N'{name}';") > 0,
                    RecordedRows = () => world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{RoleCreated}' AND Details LIKE N'%{name}%';"),
                };
            }

            case UnitKind.ReplacePermissions:
            {
                var actor = await world.NewUserAsync("replacer", await world.NewRoleAsync("replacer", PermissionAssign, P));
                var target = await world.NewRoleAsync("target");
                return new UnitCase
                {
                    Actor = actor,
                    Run = (w, db, unit) => w.Service(db, actor, advisoryAllowsAll: true, unit: unit).SetRolePermissionsAsync(new Core.Domain.Identity.RoleId(target), [P]).AsTask(),
                    ChangeLanded = async () => (await world.RoleGrantsAsync(target)).Count == 1,
                    RecordedRows = () => world.AuditRowsAsync(PermissionsUpdated, target),
                };
            }

            case UnitKind.AssignRole:
            {
                var actor = await world.NewUserAsync("assigner", await world.NewRoleAsync("assigner", RoleAssign, P));
                var target = await world.NewRoleAsync("target", P);
                var user = await world.NewUserAsync("user");
                return new UnitCase
                {
                    Actor = actor,
                    Run = (w, db, unit) => w.Service(db, actor, advisoryAllowsAll: true, unit: unit).AssignRoleAsync(new Core.Domain.Identity.UserId(user), new Core.Domain.Identity.RoleId(target)).AsTask(),
                    ChangeLanded = () => world.AssignmentExistsAsync(user, target),
                    RecordedRows = () => world.AuditRowsAsync(Assigned, user),
                };
            }

            default:
            {
                var actor = await world.NewUserAsync("remover", await world.NewRoleAsync("remover", RoleAssign, P));
                var target = await world.NewRoleAsync("target", P);
                var user = await world.NewUserAsync("user", target);
                return new UnitCase
                {
                    Actor = actor,
                    Run = (w, db, unit) => w.Service(db, actor, advisoryAllowsAll: true, unit: unit).RemoveRoleAsync(new Core.Domain.Identity.UserId(user), new Core.Domain.Identity.RoleId(target)).AsTask(),
                    ChangeLanded = async () => !await world.AssignmentExistsAsync(user, target),
                    RecordedRows = () => world.AuditRowsAsync(Removed, user),
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryUnitOnBothModes))]
    public async Task M6_an_audit_insert_failure_leaves_nothing_changed_and_nothing_recorded(UnitKind kind, bool rcsi)
    {
        var world = World(rcsi);
        var unitCase = await ArrangeAsync(world, kind);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        var attempts = new AttemptCounter();

        await using var db = world.Scratch.NewDb(failAudit, attempts);
        var act = () => unitCase.Run(world, db, NewUnit(db, new Principal(unitCase.Actor), FastOptions()));

        await act.Should().ThrowAsync<Exception>();
        failAudit.Failures.Should().BeGreaterThan(0);
        (await unitCase.ChangeLanded()).Should().BeFalse("the change and its audit row commit together, or not at all");
        (await unitCase.RecordedRows()).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(EveryUnitOnBothModes))]
    public async Task M6_a_lost_acknowledgement_is_verified_as_committed_and_not_rerun(UnitKind kind, bool rcsi)
    {
        var world = World(rcsi);
        var unitCase = await ArrangeAsync(world, kind);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using var db = world.Scratch.NewDb(lost, attempts);
        await unitCase.Run(world, db, NewUnit(db, new Principal(unitCase.Actor), FastOptions()));

        attempts.Started.Should().Be(1, "verification confirmed the commit, so the decision is not taken again");
        (await unitCase.ChangeLanded()).Should().BeTrue();
        (await unitCase.RecordedRows()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(EveryUnitOnBothModes))]
    public async Task M6_a_confirmed_rollback_reruns_once_and_commits_one_change_and_one_audit_row(UnitKind kind, bool rcsi)
    {
        var world = World(rcsi);
        var unitCase = await ArrangeAsync(world, kind);
        var rolledBack = new RolledBackCommit();
        var attempts = new AttemptCounter();

        await using var db = world.Scratch.NewDb(rolledBack, attempts);
        await unitCase.Run(world, db, NewUnit(db, new Principal(unitCase.Actor), FastOptions()));

        attempts.Started.Should().Be(2, "verification confirmed the rollback, so one rerun was allowed, and it decided again under the locks");
        (await unitCase.ChangeLanded()).Should().BeTrue();
        (await unitCase.RecordedRows()).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(EveryUnitOnBothModes))]
    public async Task M6_an_unknown_outcome_is_wholly_committed_or_absent_never_half(UnitKind kind, bool rcsi)
    {
        var world = World(rcsi);
        var unitCase = await ArrangeAsync(world, kind);
        var lost = new LostCommitAcknowledgement();
        var verification = new UnreachableVerification();

        await using var db = world.Scratch.NewDb(lost);
        var act = () => unitCase.Run(world, db, NewUnit(db, new Principal(unitCase.Actor), FastOptions(), verification: verification));

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        var landed = await unitCase.ChangeLanded();
        var recorded = await unitCase.RecordedRows();
        // The commit really happened here, so both landed; the point is that they are never one without the other.
        (landed, recorded).Should().Be((true, 1));
    }

    [Theory]
    [MemberData(nameof(EveryUnitOnBothModes))]
    public async Task M6_an_operation_lock_that_is_not_granted_changes_nothing(UnitKind kind, bool rcsi)
    {
        var world = World(rcsi);
        var unitCase = await ArrangeAsync(world, kind);
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(world.Scratch.ConnectionString, "apperture.audit-op." + operationId);

        await using var db = world.Scratch.NewDb();
        var act = () => unitCase.Run(
            world, db, NewUnit(db, new Principal(unitCase.Actor), FastOptions(), eventIds: new FixedEventIds(operationId)));

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        (await unitCase.ChangeLanded()).Should().BeFalse();
        (await unitCase.RecordedRows()).Should().Be(0);
    }
}
