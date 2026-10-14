using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.ConnectionSqlHarness;
using static Apperture.IntegrationTests.Audit.OwnershipSqlSeed;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 5: the REAL audited unit raced against the ownership writers, which was the carried-forward gap of the slice 4
/// review (finding L4): the earlier ownership race suite runs its set-up through a unit that opens a transaction and stages
/// nothing. Here every racer is a real service on a context of its own with the real unit, the real gate and the real repositories,
/// as two HTTP requests would be.
/// </summary>
/// <remarks>
/// Each race is deterministic in both orders and on both isolation modes. A separate session holds the ownership gate; the first
/// writer is started and the server's own lock view is read until it is seen waiting; only then is the second started and awaited
/// in turn; then the gate is released. The lock manager grants queued requests in arrival order, so which writer commits first is
/// fixed by the test, not by timing. After each order the pair's invariant is checked and so is the exact outcome that order must
/// produce, including the audit rows (the related context of a row is the owner as locked by the writer that wrote it). Fails,
/// never skips, without SQL Server.
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class OwnershipRealUnitRaceSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public OwnershipRealUnitRaceSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public static IEnumerable<object[]> Orders() =>
        from firstIsA in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { firstIsA, rcsi };

    // ---- An integration move racing an Application archive ----

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task An_integration_move_into_a_context_racing_its_archive_never_leaves_an_archived_application_owning_a_live_integration(bool moveFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Race source");
        var target = await SeedContextAsync(scratch, "Race target");
        var integration = await SeedIntegrationAsync(scratch, source);

        var (move, archive) = await RaceAsync(
            scratch, principal, moveFirst,
            a => a.Move.MoveIntegrationAsync(new IntegrationId(integration), new ResourceContextId(target)).AsTask(),
            b => b.Contexts.ArchiveAsync(new ResourceContextId(target), CancellationToken.None).AsTask());

        var owner = await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integration}';");
        var targetState = await scratch.ScalarAsync<string>($"SELECT State FROM ResourceContexts WHERE Id = N'{target}';");
        (targetState == ResourceContextStates.Archived && owner == target).Should().BeFalse("an archived Application must never own a live integration");

        var integrationRows = (await AuditRowsAsync(scratch, integration)).Select(r => (string)r["Action"]!).ToList();
        if (moveFirst)
        {
            move.Value!.CanMove.Should().BeTrue();
            archive.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("integration not retired");
            owner.Should().Be(target);
            targetState.Should().Be(ResourceContextStates.Active);
            integrationRows.Should().Equal(AuditActionNames.IntegrationMovedOut, AuditActionNames.IntegrationMovedIn);
            (await AuditRowsAsync(scratch, target)).Should().BeEmpty("the refused archive recorded nothing");
        }
        else
        {
            archive.Error.Should().BeNull();
            move.Value!.CanMove.Should().BeFalse();
            move.Value.Blockers.Select(b => b.Code).Should().Contain(ResourceMoveBlockerCodes.TargetArchived);
            owner.Should().Be(source);
            targetState.Should().Be(ResourceContextStates.Archived);
            integrationRows.Should().Equal(AuditActionNames.IntegrationMoveRejected);
            (await AuditRowsAsync(scratch, target)).Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.ResourceContextArchived);
        }

        _output.WriteLine($"{scratch.ExecutionMarker}; order: {(moveFirst ? "move, archive" : "archive, move")}");
    }

    // ---- An integration move racing a binding creation ----

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task An_integration_move_racing_a_binding_creation_never_leaves_a_binding_to_a_connection_the_integration_can_no_longer_reach(bool moveFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Bind source");
        var target = await SeedContextAsync(scratch, "Bind target");
        var integration = await SeedIntegrationAsync(scratch, source);
        var connection = await SeedConnectionAsync(scratch, source);

        var (move, bind) = await RaceAsync(
            scratch, principal, moveFirst,
            a => a.Move.MoveIntegrationAsync(new IntegrationId(integration), new ResourceContextId(target)).AsTask(),
            b => b.Bindings(connection).CreateBindingAsync("Sample.Mod", "slot-a", "test.contract", new ConnectionId(connection), new IntegrationId(integration)).AsTask());

        var owner = await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integration}';");
        var bindings = await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Bindings WHERE IntegrationId = N'{integration}' AND ConnectionId = N'{connection}';");
        (owner == target && bindings > 0).Should().BeFalse("an integration in the target must not hold a binding to a connection that stayed in the source");

        var integrationRows = (await AuditRowsAsync(scratch, integration)).Select(r => (string)r["Action"]!).ToList();
        if (moveFirst)
        {
            move.Value!.CanMove.Should().BeTrue();
            bind.Error.Should().BeOfType<InvalidOperationException>("the connection is no longer reachable from the integration");
            owner.Should().Be(target);
            bindings.Should().Be(0);
            integrationRows.Should().Equal(AuditActionNames.IntegrationMovedOut, AuditActionNames.IntegrationMovedIn);
        }
        else
        {
            bind.Error.Should().BeNull();
            move.Value!.CanMove.Should().BeFalse();
            move.Value.Blockers.Select(b => b.Code).Should().Contain(ResourceMoveBlockerCodes.BindingConnectionUnreachable);
            owner.Should().Be(source);
            bindings.Should().Be(1);
            integrationRows.Should().Equal(AuditActionNames.IntegrationMoveRejected);
            (await AuditRowsAsync(scratch, bind.Value!.Id.Value)).Should().ContainSingle().Which["ResourceContextId"].Should().Be(source);
        }

        _output.WriteLine($"{scratch.ExecutionMarker}; order: {(moveFirst ? "move, bind" : "bind, move")}");
    }

    // ---- An integration move racing an attached schedule creation ----

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task An_integration_move_racing_an_attached_schedule_creation_leaves_every_schedule_with_its_integration(bool moveFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Sched source");
        var target = await SeedContextAsync(scratch, "Sched target");
        var integration = await SeedIntegrationAsync(scratch, source);
        var scheduleId = "sch-" + Guid.NewGuid().ToString("N")[..12];
        var definition = new ScheduleDefinition(
            new ScheduleId(scheduleId), "Raced schedule", "0 1 * * *", "UTC", Enabled: false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"), ExecutionPolicyRef: null, Version: 1, new IntegrationId(integration));

        var (move, create) = await RaceAsync(
            scratch, principal, moveFirst,
            a => a.Move.MoveIntegrationAsync(new IntegrationId(integration), new ResourceContextId(target)).AsTask(),
            b => b.Schedules.CreateScheduleWithAssignmentAsync(definition, new RuntimePlaneId("local"), null, CancellationToken.None).AsTask());

        move.Value!.CanMove.Should().BeTrue();
        create.Error.Should().BeNull();
        var owner = await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integration}';");
        owner.Should().Be(target);
        (await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM ScheduleDefinitions WHERE Id = N'{scheduleId}';"))
            .Should().Be(owner, "a schedule created while the integration moved is owned by the integration's context, whichever came first");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM ScheduleAssignments WHERE ScheduleId = N'{scheduleId}';")).Should().Be(1);

        var movedOut = (await AuditRowsAsync(scratch, integration)).Single(r => (string)r["Action"]! == AuditActionNames.IntegrationMovedOut);
        Detail(movedOut, "schedulesMoved").Should().Be(moveFirst ? "0" : "1", "the move saw the schedule only when its creation committed first");
        (await AuditRowsAsync(scratch, scheduleId)).Single()["ResourceContextId"].Should().Be(moveFirst ? target : source,
            "the creation recorded the owner it locked: the target after the move, the source before it");
        _output.WriteLine($"{scratch.ExecutionMarker}; order: {(moveFirst ? "move, create" : "create, move")}");
    }

    // ---- An integration move racing an integration update ----

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task An_integration_move_racing_an_activation_applies_both_and_each_row_names_the_owner_its_writer_locked(bool moveFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Update source");
        var target = await SeedContextAsync(scratch, "Update target");
        var integration = await SeedIntegrationAsync(scratch, source, IntegrationState.Inactive);

        var (move, activate) = await RaceAsync(
            scratch, principal, moveFirst,
            a => a.Move.MoveIntegrationAsync(new IntegrationId(integration), new ResourceContextId(target)).AsTask(),
            b => b.Integrations.ActivateIntegrationAsync(new IntegrationId(integration)).AsTask());

        move.Value!.CanMove.Should().BeTrue();
        activate.Error.Should().BeNull();
        (await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integration}';")).Should().Be(target);
        (await scratch.ScalarAsync<int>($"SELECT State FROM Integrations WHERE Id = N'{integration}';")).Should().Be((int)IntegrationState.Active);
        var rows = await AuditRowsAsync(scratch, integration);
        rows.Select(r => (string)r["Action"]!).Should().BeEquivalentTo(
            AuditActionNames.IntegrationMovedOut, AuditActionNames.IntegrationMovedIn, AuditActionNames.IntegrationActivated);
        rows.Single(r => (string)r["Action"]! == AuditActionNames.IntegrationActivated)["ResourceContextId"].Should().Be(moveFirst ? target : source);
        _output.WriteLine($"{scratch.ExecutionMarker}; order: {(moveFirst ? "move, activate" : "activate, move")}");
    }

    // ---- An Application archive racing a connection creation ----

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task An_archive_racing_a_connection_creation_never_leaves_an_archived_application_owning_a_live_connection(bool archiveFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var app = await SeedContextAsync(scratch, "Archive race");
        var name = "Raced-" + Guid.NewGuid().ToString("N")[..8];

        var (archive, create) = await RaceAsync(
            scratch, principal, archiveFirst,
            a => a.Contexts.ArchiveAsync(new ResourceContextId(app), CancellationToken.None).AsTask(),
            b => b.Connections.CreateConnectionAsync(new CreateConnectionRequest(
                Sql, name, new Dictionary<string, string> { ["Host"] = HostSentinel }, new Dictionary<string, string>(),
                ResourceContextId: new ResourceContextId(app))).AsTask());

        var state = await scratch.ScalarAsync<string>($"SELECT State FROM ResourceContexts WHERE Id = N'{app}';");
        var live = await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Connections WHERE ResourceContextId = N'{app}' AND State = {(int)ConnectionState.Active};");
        (state == ResourceContextStates.Archived && live > 0).Should().BeFalse("an archived Application must never own a live connection");

        if (archiveFirst)
        {
            archive.Error.Should().BeNull();
            create.Error.Should().BeOfType<ResourceContextArchivedException>();
            live.Should().Be(0);
            state.Should().Be(ResourceContextStates.Archived);
        }
        else
        {
            create.Error.Should().BeNull();
            archive.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("active connection");
            live.Should().Be(1);
            state.Should().Be(ResourceContextStates.Active);
        }

        _output.WriteLine($"{scratch.ExecutionMarker}; order: {(archiveFirst ? "archive, create" : "create, archive")}");
    }

    // ---- Two moves of one connection ----

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task Two_moves_of_one_connection_serialize_and_the_second_pair_starts_where_the_first_ended(bool firstIsA, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Chain source");
        var targetA = await SeedContextAsync(scratch, "Chain A");
        var targetB = await SeedContextAsync(scratch, "Chain B");
        var connection = await SeedConnectionAsync(scratch, source);

        var (a, b) = await RaceAsync(
            scratch, principal, firstIsA,
            x => x.Move.MoveConnectionAsync(new ConnectionId(connection), new ResourceContextId(targetA)).AsTask(),
            y => y.Move.MoveConnectionAsync(new ConnectionId(connection), new ResourceContextId(targetB)).AsTask());

        a.Value!.CanMove.Should().BeTrue();
        b.Value!.CanMove.Should().BeTrue();
        var (firstTarget, secondTarget) = firstIsA ? (targetA, targetB) : (targetB, targetA);
        (await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Connections WHERE Id = N'{connection}';")).Should().Be(secondTarget, "the writer that committed last owns the result");

        var movedOut = (await AuditRowsAsync(scratch, connection)).Where(r => (string)r["Action"]! == AuditActionNames.ConnectionMovedOut).ToList();
        movedOut.Should().HaveCount(2);
        Detail(movedOut[0], "sourceContextId").Should().Be(source);
        Detail(movedOut[0], "targetContextId").Should().Be(firstTarget);
        Detail(movedOut[1], "sourceContextId").Should().Be(firstTarget, "the second move read the connection after the first committed");
        Detail(movedOut[1], "targetContextId").Should().Be(secondTarget);
        _output.WriteLine($"{scratch.ExecutionMarker}; order: {(firstIsA ? "A then B" : "B then A")}");
    }

    // ---- A connection move racing a rotation of its credential (queued on the connection row) ----

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task A_connection_move_racing_a_rotation_keeps_both_and_the_rotation_row_names_the_owner_it_locked(bool moveFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var target = await SeedContextAsync(scratch, "Rotate target");
        var seeded = await SeedConnectionAsync(scratch, principal, withMaterial: true);

        // Both writers need the connection row: held by another session, they queue on it in the order they arrive.
        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Connections", seeded.ConnectionId);
        Task<Outcome<ResourceMovePreview>> move = null!;
        Task<Outcome<Connection>> rotate = null!;
        Task<Outcome<ResourceMovePreview>> StartMove() => StartAsync(scratch, principal, s => s.Move.MoveConnectionAsync(new ConnectionId(seeded.ConnectionId), new ResourceContextId(target)).AsTask());
        Task<Outcome<Connection>> StartRotate() => StartAsync(scratch, principal, s => s.Connections.UpdateConnectionAsync(
            new ConnectionId(seeded.ConnectionId), new UpdateConnectionRequest(null, null, new Dictionary<string, string> { ["Password"] = NewSecret })).AsTask());
        if (moveFirst)
        {
            move = StartMove();
            await holder.WaitUntilBlockedAsync(scratch, "Connections", move);
            rotate = StartRotate();
            await holder.WaitUntilBlockedAsync(scratch, "Connections", Task.WhenAny(move, rotate), minimumWaiters: 2);
        }
        else
        {
            rotate = StartRotate();
            await holder.WaitUntilBlockedAsync(scratch, "Connections", rotate);
            move = StartMove();
            await holder.WaitUntilBlockedAsync(scratch, "Connections", Task.WhenAny(move, rotate), minimumWaiters: 2);
        }

        await holder.CommitAsync();
        var moved = await move;
        var rotated = await rotate;

        moved.Error.Should().BeNull();
        moved.Value!.CanMove.Should().BeTrue();
        rotated.Error.Should().BeNull();
        (await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Connections WHERE Id = N'{seeded.ConnectionId}';")).Should().Be(target);
        (await ResolveAsync(scratch, seeded.SecretRef!)).Should().Be(NewSecret, "moving contexts never moves or copies secret material, and the rotation is kept");
        var rows = await AuditRowsAsync(scratch, seeded.ConnectionId);
        rows.Select(r => (string)r["Action"]!).Should().BeEquivalentTo(
            AuditActionNames.ConnectionMovedOut, AuditActionNames.ConnectionMovedIn, AuditActionNames.ConnectionSecretRotated, AuditActionNames.ConnectionSecretStored);
        rows.Single(r => (string)r["Action"]! == AuditActionNames.ConnectionSecretRotated)["ResourceContextId"].Should().Be(moveFirst ? target : ResourceContextIds.SharedValue);
        _output.WriteLine($"{scratch.ExecutionMarker}; order: {(moveFirst ? "move, rotate" : "rotate, move")}");
    }

    // ---- wiring ----

    private sealed record Outcome<T>(T? Value, Exception? Error);

    /// <summary>One real service stack on a context of its own, as one HTTP request would have.</summary>
    private sealed class Stack(AppertureDbContext db, AuditSqlScratch scratch, IPrincipalContext principal)
    {
        private IAuditedUnitOfWork Unit => NewUnit(db, principal);
        private ResourceContextService ContextService => new(new SqlResourceContextRepository(db), TimeProvider.System);
        private SqlResourceOwnershipMutationGate Gate => new(db);

        public ResourceOwnershipMoveService Move => new(
            principal, new AllowAll(), new SqlIntegrationRepository(db), new SqlConnectionRepository(db), new SqlScheduleRepository(db),
            new SqlBindingRepository(db), ContextService, Gate, Unit, new SqlAuditEventWriter(scratch.Options(), principals: principal));

        public ResourceContextManagementService Contexts => new(
            ContextService, new SqlResourceContextRepository(db), principal, new AllowAll(), Gate, Unit);

        public IntegrationManagementService Integrations => new(
            principal, new AllowAll(), new SqlIntegrationRepository(db), ContextService, db, Gate, auditedUnitOfWork: Unit);

        public SqlScheduleManagementService Schedules => new(
            new SqlScheduleRepository(db), new StubClaimOptions(), TimeProvider.System, ContextService, new SqlIntegrationRepository(db), db, Gate, principal, Unit);

        public ConnectionManagementService Connections => ConnectionSqlHarness.Service(db, principal, Unit);

        public BindingService Bindings(string eligibleConnectionId) => new(
            principal, new AllowAllResource(), new SqlBindingRepository(db), new SqlConnectionRepository(db),
            new OneConnectionEligible(db, eligibleConnectionId), new SqlIntegrationRepository(db), db, Gate, Unit);
    }

    private static Task<Outcome<T>> StartAsync<T>(AuditSqlScratch scratch, IPrincipalContext principal, Func<Stack, Task<T>> body) =>
        Task.Run(async () =>
        {
            await using var db = scratch.NewDb();
            try
            {
                return new Outcome<T>(await body(new Stack(db, scratch, principal)), null);
            }
            catch (Exception ex)
            {
                return new Outcome<T>(default, ex);
            }
        });

    /// <summary>
    /// Runs two writers that both need the ownership gate in a fixed order: the first is started and seen waiting on the held gate,
    /// then the second, then the gate is released. Each writer's outcome (its result or the exception it ended with) is returned.
    /// </summary>
    private static async Task<(Outcome<TA> A, Outcome<TB> B)> RaceAsync<TA, TB>(
        AuditSqlScratch scratch, IPrincipalContext principal, bool aFirst, Func<Stack, Task<TA>> a, Func<Stack, Task<TB>> b)
    {
        await using var gate = await OwnershipGateRace.HoldAsync(scratch);
        Task<Outcome<TA>> first;
        Task<Outcome<TB>> second;
        if (aFirst)
        {
            first = StartAsync(scratch, principal, a);
            await OwnershipGateRace.WaitForWaitersAsync(scratch, 1, first);
            second = StartAsync(scratch, principal, b);
            await OwnershipGateRace.WaitForWaitersAsync(scratch, 2, first, second);
            await gate.ReleaseAsync();
            return (await first, await second);
        }

        var firstB = StartAsync(scratch, principal, b);
        await OwnershipGateRace.WaitForWaitersAsync(scratch, 1, firstB);
        var secondA = StartAsync(scratch, principal, a);
        await OwnershipGateRace.WaitForWaitersAsync(scratch, 2, firstB, secondA);
        await gate.ReleaseAsync();
        return (await secondA, await firstB);
    }

    private sealed class AllowAllResource : IAuthorizationService
    {
        public ValueTask<bool> EvaluateAsync(
            IActor actor, PermissionDescriptor permission, ResourceDescriptor? resource,
            IGovernedResourceIdentity? resourceIdentity = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    /// <summary>The one connection under test is eligible: these races are about ownership, not about connector contracts.</summary>
    private sealed class OneConnectionEligible(AppertureDbContext db, string connectionId) : IBindingEligibilityService
    {
        public async ValueTask<IReadOnlyList<Connection>> GetEligibleConnectionsAsync(
            string moduleId, string slotName, string contractType, ResourceContextId? owningResourceContextId = null, CancellationToken cancellationToken = default)
        {
            var connection = await new SqlConnectionRepository(db).GetByIdAsync(new ConnectionId(connectionId), cancellationToken);
            return connection is null ? [] : [connection];
        }
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }
}
