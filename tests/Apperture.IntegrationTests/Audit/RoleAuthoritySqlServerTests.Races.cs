using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2a, A10.2.10: the focused race tests RT-1 to RT-9 (IAM-10 gate P-5). Each runs on both isolation modes with a
// deterministic interleaving: one side is parked with its locks held (an interceptor holds it just after its last lock, or just
// before its write) while the other is shown, by the server's own wait list, to be waiting; then the first is released. The check
// made before the unit is switched off (advisoryAllowsAll) so that only the decision under the lock can refuse. The audit
// recorder's own unindexed count (AUD-1.1) is not the lock under test, so the waits are always on the authority rows.
public sealed partial class RoleAuthoritySqlServerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // Where two operations by different actors race, the second may first wait on the assignments table rather than the roles table:
    // a key-range lock over one user's assignments also covers the gap up to the next key, which can be another user's row, depending
    // on the ids. Either wait is the serialization the lock set exists to give (and costs only a brief wait), so those cases accept both.

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try
        {
            await task.WaitAsync(Patience);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private Task Assign(RoleAuthorityWorld world, string actor, string user, string role, params IInterceptor[] interceptors) =>
        Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(interceptors);
            await world.Service(db, actor, advisoryAllowsAll: true).AssignRoleAsync(new UserId(user), new RoleId(role));
        });

    private Task Replace(RoleAuthorityWorld world, string actor, string role, PermissionDescriptor[] grants, params IInterceptor[] interceptors) =>
        Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(interceptors);
            await world.Service(db, actor, advisoryAllowsAll: true).SetRolePermissionsAsync(new RoleId(role), grants);
        });

    // ---- RT-1: the actor's assignment is removed ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT1_the_actors_assignment_removed_by_a_writer_first_is_seen_by_the_decision(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, holdsP, target, user) = await ArrangeAssignAsync(world);
        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        await writer.ExecuteAsync($"DELETE FROM UserRoles WHERE UserId = N'{actor}' AND RoleId = N'{holdsP}';");

        var operation = Assign(world, actor, user, target);
        await world.WaitUntilBlockedOnAsync("UserRoles", operation);
        await writer.CommitAsync();
        var outcome = await CaptureAsync(operation);

        outcome.Should().BeOfType<AuthorizationRefusalException>().Which.Code.Should().Be("assignment_exceeds_authority");
        (await world.AssignmentExistsAsync(user, target)).Should().BeFalse();
        (await world.AuditRowsAsync(AssignmentRejected, user)).Should().Be(1);
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(0);
        _output.WriteLine(world.Scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT1_the_actors_assignment_cannot_be_removed_while_an_operation_that_relies_on_it_is_in_flight(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, holdsP, target, user) = await ArrangeAssignAsync(world);
        var pause = PauseAt.AfterAssignmentKeyLock();

        var operation = Assign(world, actor, user, target, pause);
        await pause.Reached.WaitAsync(Patience);

        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        var removal = Task.Run(() => writer.ExecuteAsync($"DELETE FROM UserRoles WHERE UserId = N'{actor}' AND RoleId = N'{holdsP}';"));
        await world.WaitUntilBlockedOnAsync("UserRoles", removal);
        removal.IsCompleted.Should().BeFalse("the operation holds the actor's assignment");

        pause.Release();
        await operation.WaitAsync(Patience);
        await removal.WaitAsync(Patience);
        await writer.CommitAsync();

        (await world.AssignmentExistsAsync(user, target)).Should().BeTrue("the operation was decided on the state before the removal");
        (await world.AssignmentExistsAsync(actor, holdsP)).Should().BeFalse("and the removal applied after it");
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(1);
    }

    private static async Task<(string Actor, string HoldsP, string Target, string User)> ArrangeAssignAsync(RoleAuthorityWorld world)
    {
        var mayAssign = await world.NewRoleAsync("may-assign", RoleAssign);
        var holdsP = await world.NewRoleAsync("holds-p", P);
        var actor = await world.NewUserAsync("actor", mayAssign, holdsP);
        var target = await world.NewRoleAsync("target", P);
        var user = await world.NewUserAsync("user");
        return (actor, holdsP, target, user);
    }

    // ---- RT-2: a permission is removed from a role the actor holds ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT2_a_permission_removed_from_the_actors_role_by_a_writer_first_is_seen_by_the_decision(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, holdsP, target) = await ArrangeReplaceAsync(world);
        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        await writer.ExecuteAsync($"UPDATE Roles SET PermissionsJson = N'[]' WHERE Id = N'{holdsP}';");

        var operation = Replace(world, actor, target, [P]);
        await world.WaitUntilBlockedOnAsync("Roles", operation);
        await writer.CommitAsync();
        var outcome = await CaptureAsync(operation);

        outcome.Should().BeOfType<AuthorizationRefusalException>().Which.Code.Should().Be("grant_exceeds_authority");
        (await world.RoleGrantsAsync(target)).Should().BeEmpty();
        (await world.AuditRowsAsync(MutationRejected, target)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT2_a_permission_cannot_be_removed_from_the_actors_role_while_a_replacement_that_relies_on_it_is_in_flight(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, holdsP, target) = await ArrangeReplaceAsync(world);
        var pause = PauseAt.AfterRoleLock(target);

        var operation = Replace(world, actor, target, [P], pause);
        await pause.Reached.WaitAsync(Patience);

        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        var removal = Task.Run(() => writer.ExecuteAsync($"UPDATE Roles SET PermissionsJson = N'[]' WHERE Id = N'{holdsP}';"));
        await world.WaitUntilBlockedOnAsync("Roles", removal);

        pause.Release();
        await operation.WaitAsync(Patience);
        await removal.WaitAsync(Patience);
        await writer.CommitAsync();

        (await world.RoleGrantsAsync(target)).Should().Equal(P);
        (await world.AuditRowsAsync(PermissionsUpdated, target)).Should().Be(1);
    }

    /// <summary>The target sorts last (<c>zz</c>), so a pause after its lock is a pause after every lock of the operation.</summary>
    private static async Task<(string Actor, string HoldsP, string Target)> ArrangeReplaceAsync(RoleAuthorityWorld world)
    {
        var mayAssign = await world.NewRoleAsync("may-assign-permissions", PermissionAssign);
        var holdsP = await world.NewRoleAsync("holds-p", P);
        var actor = await world.NewUserAsync("actor", mayAssign, holdsP);
        var target = await world.NewRoleAsync("zz-target");
        return (actor, holdsP, target);
    }

    // ---- RT-3: the target role is edited while it is assigned ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT3_a_target_role_edited_first_is_assigned_only_if_the_actor_holds_what_it_now_contains(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, _, target, user) = await ArrangeAssignAsync(world);
        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        await writer.ExecuteAsync(
            $"UPDATE Roles SET PermissionsJson = N'{StoredRolePermissions.Serialize([P, Q])}' WHERE Id = N'{target}';");

        var operation = Assign(world, actor, user, target);
        await world.WaitUntilBlockedOnAsync("Roles", operation);
        await writer.CommitAsync();
        var outcome = await CaptureAsync(operation);

        var refusal = outcome.Should().BeOfType<AuthorizationRefusalException>().Subject;
        refusal.Missing.Should().Equal("integration.read@Integration:instance");
        (await world.AssignmentExistsAsync(user, target)).Should().BeFalse("never R plus Q assigned by an actor lacking Q");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT3_a_target_role_cannot_be_edited_between_the_decision_and_the_assignment(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, _, target, user) = await ArrangeAssignAsync(world);
        var pause = PauseAt.AfterAssignmentKeyLock();

        var operation = Assign(world, actor, user, target, pause);
        await pause.Reached.WaitAsync(Patience);

        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        var edit = Task.Run(() => writer.ExecuteAsync(
            $"UPDATE Roles SET PermissionsJson = N'{StoredRolePermissions.Serialize([P, Q])}' WHERE Id = N'{target}';"));
        await world.WaitUntilBlockedOnAsync("Roles", edit);

        pause.Release();
        await operation.WaitAsync(Patience);
        await edit.WaitAsync(Patience);
        await writer.CommitAsync();

        (await world.AssignmentExistsAsync(user, target)).Should().BeTrue("assigned on the role as it was when the actor held all of it");
        (await world.RoleGrantsAsync(target)).Should().BeEquivalentTo([P, Q], "and edited after it");
    }

    // ---- RT-4: two replacements of the same role by different actors ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT4_two_replacements_serialize_and_the_second_decides_on_what_the_first_committed(bool rcsi)
    {
        var world = World(rcsi);
        var (actorA, actorB, target) = await ArrangeTwoReplacersAsync(world);
        var pause = PauseAt.AfterRoleLock(target);

        // A (holds P) replaces with {P}; B (holds Q) replaces with {P, Q}. B may add only what it holds, and what it adds depends on A.
        var first = Replace(world, actorA, target, [P], pause);
        await pause.Reached.WaitAsync(Patience);
        var second = Replace(world, actorB, target, [P, Q]);
        await world.WaitUntilBlockedOnAnyAsync(second, "Roles", "UserRoles");
        second.IsCompleted.Should().BeFalse();

        pause.Release();
        await first.WaitAsync(Patience);
        await second.WaitAsync(Patience); // B adds only Q now: P is already there

        (await world.RoleGrantsAsync(target)).Should().BeEquivalentTo([P, Q]);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT4_the_other_order_refuses_the_replacement_that_adds_a_permission_its_actor_lacks(bool rcsi)
    {
        var world = World(rcsi);
        var (actorA, actorB, target) = await ArrangeTwoReplacersAsync(world);
        var pause = PauseAt.AfterRoleLock(target);

        var first = Replace(world, actorB, target, [P, Q], pause);
        await pause.Reached.WaitAsync(Patience);
        var second = Replace(world, actorA, target, [P]);
        await world.WaitUntilBlockedOnAnyAsync(second, "Roles", "UserRoles");

        pause.Release();
        var firstOutcome = await CaptureAsync(first);
        await second.WaitAsync(Patience);

        firstOutcome.Should().BeOfType<AuthorizationRefusalException>("B lacks P, which this replacement adds");
        (await world.RoleGrantsAsync(target)).Should().Equal(P);
    }

    private static async Task<(string ActorA, string ActorB, string Target)> ArrangeTwoReplacersAsync(RoleAuthorityWorld world)
    {
        var target = await world.NewRoleAsync("zz-shared-target");
        var actorA = await world.NewUserAsync("replacer-a", await world.NewRoleAsync("a-assign", PermissionAssign), await world.NewRoleAsync("a-holds-p", P));
        var actorB = await world.NewUserAsync("replacer-b", await world.NewRoleAsync("b-assign", PermissionAssign), await world.NewRoleAsync("b-holds-q", Q));
        return (actorA, actorB, target);
    }

    // ---- RT-5: the actor edits a role they hold ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT5_an_actor_editing_a_role_they_hold_locks_it_once_and_is_decided_on_what_it_held_before_the_edit(bool rcsi)
    {
        var world = World(rcsi);
        var own = await world.NewRoleAsync("own", PermissionAssign, P);
        var holdsQ = await world.NewRoleAsync("holds-q", Q);
        var actor = await world.NewUserAsync("self-editor", own, holdsQ);

        // Adding Q (held through the other role) to the role being edited is allowed.
        await Replace(world, actor, own, [PermissionAssign, P, Q]).WaitAsync(Patience);
        (await world.RoleGrantsAsync(own)).Should().BeEquivalentTo([PermissionAssign, P, Q]);

        // Adding S, which nothing the actor holds provides, is refused: the edit does not widen what the decision may rely on.
        var outcome = await CaptureAsync(Replace(world, actor, own, [PermissionAssign, P, Q, S]));
        outcome.Should().BeOfType<AuthorizationRefusalException>().Which.Missing.Should().Equal("schedule.manage@Schedule:instance");
        (await world.RoleGrantsAsync(own)).Should().BeEquivalentTo([PermissionAssign, P, Q]);
    }

    // ---- RT-6: two identical assignments ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT6_two_identical_assignments_leave_one_row_and_one_audit_row_and_the_second_is_a_silent_no_op(bool rcsi)
    {
        var world = World(rcsi);
        var actor1 = await world.NewUserAsync("assigner-1", await world.NewRoleAsync("assigner-1", RoleAssign, P));
        var actor2 = await world.NewUserAsync("assigner-2", await world.NewRoleAsync("assigner-2", RoleAssign, P));
        var target = await world.NewRoleAsync("target", P);
        var user = await world.NewUserAsync("user");
        var pause = PauseAt.AfterAssignmentKeyLock();

        var first = Assign(world, actor1, user, target, pause);
        await pause.Reached.WaitAsync(Patience);
        var second = Assign(world, actor2, user, target);
        await world.WaitUntilBlockedOnAnyAsync(second, "Roles", "UserRoles");

        pause.Release();
        await first.WaitAsync(Patience);
        await second.WaitAsync(Patience);

        (await world.AssignmentCountAsync(user, target)).Should().Be(1);
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(1, "the second request found the assignment and recorded nothing");
    }

    /// <summary>The assignment key lock matters against a writer that takes no role row lock: a plain insert of the same pair.</summary>
    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT6_a_plain_insert_of_the_same_pair_committed_first_makes_the_assignment_a_no_op(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, _, target, user) = await ArrangeAssignAsync(world);
        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        await writer.ExecuteAsync($"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{user}', N'{target}');");

        var operation = Assign(world, actor, user, target);
        await world.WaitUntilBlockedOnAsync("UserRoles", operation);
        await writer.CommitAsync();
        await operation.WaitAsync(Patience);

        (await world.AssignmentCountAsync(user, target)).Should().Be(1);
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(0, "the operation found the pair already assigned");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT6_a_plain_insert_of_the_same_pair_cannot_slip_in_after_the_check_and_loses_to_the_primary_key(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, _, target, user) = await ArrangeAssignAsync(world);
        var pause = PauseAt.AfterAssignmentKeyLock();

        var operation = Assign(world, actor, user, target, pause);
        await pause.Reached.WaitAsync(Patience);
        await using var writer = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        var insert = Task.Run(() => writer.ExecuteAsync($"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{user}', N'{target}');"));
        await world.WaitUntilBlockedOnAsync("UserRoles", insert);

        pause.Release();
        await operation.WaitAsync(Patience);
        var failure = await CaptureAsync(insert);
        await writer.RollbackAsync(); // a failed statement leaves the transaction open and its lock held

        failure.Should().BeOfType<SqlException>().Which.Number.Should().Be(2627, "the plain insert loses to the primary key: the operation's pair was committed first");
        (await world.AssignmentCountAsync(user, target)).Should().Be(1, "never a second row");
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(1);
    }

    // ---- RT-7: a deadlock victim reruns once ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT7_a_deadlock_victim_reruns_once_recomputes_the_decision_and_writes_no_refusal_row(bool rcsi)
    {
        var world = World(rcsi);
        var actorRole = await world.NewRoleAsync("assign-and-p", RoleAssign, P);
        var actor = await world.NewUserAsync("actor", actorRole);
        var target = await world.NewRoleAsync("target", P);
        var user = await world.NewUserAsync("user");

        var log = new DeadlockLog();
        var pause = PauseAt.AfterActorAssignmentLock();
        var attempts = new AttemptCounter();
        var snapshotLocks = new StatementCounter(IsActorAssignmentLock);
        var operation = Task.Run(async () =>
        {
            await using var db = world.NewDeadlockAwareDb(log, pause, attempts, snapshotLocks);
            await world.Service(db, actor, advisoryAllowsAll: true).AssignRoleAsync(new UserId(user), new RoleId(target));
        });
        await pause.Reached.WaitAsync(Patience);

        // A competing session in the order the role delete takes its locks: the role row first, then the assignments of that role.
        // It wins any deadlock, so the operation is the victim.
        await using var competitor = await RawSession.BeginAsync(world.Scratch.ConnectionString, highDeadlockPriority: true);
        await competitor.ExecuteAsync($"UPDATE Roles SET Name = Name WHERE Id = N'{actorRole}';");
        var competitorSecond = Task.Run(() => competitor.ExecuteAsync("UPDATE UserRoles SET RoleId = RoleId WHERE RoleId = @r;", ("@r", actorRole)));
        await world.WaitUntilBlockedOnAsync("UserRoles", competitorSecond);

        pause.Release(); // the operation now asks for the role row the competitor holds, which closes the cycle
        var deadline = DateTime.UtcNow.Add(Patience);
        while (Volatile.Read(ref log.Victims) == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        log.Victims.Should().Be(1, "the server chose the operation as the deadlock victim");

        await competitorSecond.WaitAsync(Patience);
        await competitor.CommitAsync();
        await operation.WaitAsync(Patience);

        attempts.Started.Should().Be(2, "one attempt lost the deadlock and the strategy reran it once");
        snapshotLocks.Count.Should().Be(2, "the snapshot, and so the decision, was taken again by the rerun");
        (await world.AssignmentExistsAsync(user, target)).Should().BeTrue();
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(1);
        (await world.AuditRowsAsync(AssignmentRejected, user)).Should().Be(0, "a deadlock is a transient failure, never a refusal");
    }

    // ---- RT-8: the startup repair writes the administrator role while an assignment of it is in flight ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT8_the_startup_repair_waits_for_an_assignment_of_the_administrator_role_that_is_in_flight(bool rcsi)
    {
        var world = World(rcsi);
        // Leave the role short of what the repair adds, so the repair has a write to make.
        await world.Scratch.ExecuteAsync(
            $"UPDATE Roles SET PermissionsJson = N'{StoredRolePermissions.Serialize([P])}' WHERE Id = N'{AdminRoleId}';");
        var administrator = await world.NewUserAsync("administrator", AdminRoleId);
        var promoted = await world.NewUserAsync("promoted");
        var pause = PauseAt.AfterAssignmentKeyLock();

        var operation = Assign(world, administrator, promoted, AdminRoleId, pause);
        await pause.Reached.WaitAsync(Patience);
        var repair = Task.Run(() => RunStartupRepairAsync(world));
        await world.WaitUntilBlockedOnAsync("Roles", repair);
        repair.IsCompleted.Should().BeFalse("the assignment holds the administrator role row");

        pause.Release();
        await operation.WaitAsync(Patience);
        await repair.WaitAsync(Patience);

        (await world.AssignmentExistsAsync(promoted, AdminRoleId)).Should().BeTrue();
        (await world.RoleGrantsAsync(AdminRoleId)).Should().Contain(Grant("schedule.manage", "Schedule"), "the repair then wrote the role");
        (await AssignedRowSensitivityAsync(world, promoted)).Should().Be("standard", "the assignment decided on the role as it was before the repair");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT8_an_assignment_of_the_administrator_role_waits_for_a_repair_in_flight_and_decides_on_the_repaired_role(bool rcsi)
    {
        var world = World(rcsi);
        await world.Scratch.ExecuteAsync(
            $"UPDATE Roles SET PermissionsJson = N'{StoredRolePermissions.Serialize([P])}' WHERE Id = N'{AdminRoleId}';");
        var administrator = await world.NewUserAsync("administrator", AdminRoleId);
        var promoted = await world.NewUserAsync("promoted");
        var pause = PauseAt.AfterRoleLock(AdminRoleId);

        var repair = Task.Run(() => RunStartupRepairAsync(world, pause));
        await pause.Reached.WaitAsync(Patience);
        var operation = Assign(world, administrator, promoted, AdminRoleId);
        await world.WaitUntilBlockedOnAsync("Roles", operation);
        operation.IsCompleted.Should().BeFalse("the repair holds the administrator role row");
        (await world.AssignmentExistsAsync(promoted, AdminRoleId)).Should().BeFalse();

        pause.Release();
        await repair.WaitAsync(Patience);
        await operation.WaitAsync(Patience);

        (await world.AssignmentExistsAsync(promoted, AdminRoleId)).Should().BeTrue();
        (await world.RoleGrantsAsync(AdminRoleId)).Should().Contain(Grant("schedule.manage", "Schedule"));
        (await AssignedRowSensitivityAsync(world, promoted)).Should().Be("critical", "the assignment woke to the repaired role and decided on that");
    }

    private static async Task<string?> AssignedRowSensitivityAsync(RoleAuthorityWorld world, string user)
    {
        var rows = await world.AuditRowDetailsAsync(Assigned, user);
        var details = rows.Should().ContainSingle().Subject["Details"]!.ToString()!;
        using var json = System.Text.Json.JsonDocument.Parse(details);
        return json.RootElement.TryGetProperty("maxSensitivity", out var value) ? value.GetString() : null;
    }

    private static async Task RunStartupRepairAsync(RoleAuthorityWorld world, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => world.Scratch.NewDb(interceptors));
        services.AddScoped<IRoleRepository>(sp => new SqlRoleRepository(sp.GetRequiredService<AppertureDbContext>()));
        services.AddScoped<IAuditedUnitOfWork>(sp => NewUnit(sp.GetRequiredService<AppertureDbContext>(), null));
        services.AddSingleton(CatalogRegistry);
        await using var provider = services.BuildServiceProvider();
        await new PlatformAdminRoleRepairService(provider.GetRequiredService<IServiceScopeFactory>()).StartAsync(CancellationToken.None);
    }

    // ---- RT-9: the target role is deleted while it is assigned ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT9_a_role_cannot_be_deleted_while_an_assignment_of_it_is_in_flight_and_is_then_refused_as_assigned(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, _, target, user) = await ArrangeAssignAsync(world);
        var deleter = await world.NewUserAsync("deleter", await world.NewRoleAsync("deleter", RoleManage));
        var pause = PauseAt.AfterAssignmentKeyLock();

        var assign = Assign(world, actor, user, target, pause);
        await pause.Reached.WaitAsync(Patience);
        var delete = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb();
            await world.Service(db, deleter, advisoryAllowsAll: true).DeleteRoleAsync(new RoleId(target));
        });
        await world.WaitUntilBlockedOnAsync("Roles", delete);

        pause.Release();
        await assign.WaitAsync(Patience);
        var outcome = await CaptureAsync(delete);

        outcome.Should().BeOfType<RoleLifecycleException>().Which.Code.Should().Be(RoleLifecycleErrorCodes.AssignmentsRemain);
        (await world.RoleExistsAsync(target)).Should().BeTrue();
        (await world.AssignmentExistsAsync(user, target)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task RT9_an_assignment_that_wakes_to_a_deleted_role_is_not_found_and_assigns_nothing(bool rcsi)
    {
        var world = World(rcsi);
        var (actor, _, target, user) = await ArrangeAssignAsync(world);
        var deleter = await world.NewUserAsync("deleter", await world.NewRoleAsync("deleter", RoleManage));
        var pause = PauseAt.BeforeStatement("DELETE FROM [Roles]");

        var delete = Task.Run(async () =>
        {
            await using var db = world.Scratch.NewDb(pause);
            await world.Service(db, deleter, advisoryAllowsAll: true).DeleteRoleAsync(new RoleId(target));
        });
        await pause.Reached.WaitAsync(Patience);
        var assign = Assign(world, actor, user, target);
        await world.WaitUntilBlockedOnAnyAsync(assign, "Roles", "UserRoles");

        pause.Release();
        await delete.WaitAsync(Patience);
        var outcome = await CaptureAsync(assign);

        outcome.Should().BeOfType<GovernanceNotFoundException>().Which.Kind.Should().Be(GovernanceNotFoundKinds.Role);
        (await world.RoleExistsAsync(target)).Should().BeFalse();
        (await world.AssignmentExistsAsync(user, target)).Should().BeFalse();
        (await world.AuditRowsAsync(Assigned, user)).Should().Be(0);
    }
}
