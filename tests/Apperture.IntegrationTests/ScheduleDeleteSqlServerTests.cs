using Apperture.ControlPlane.Security;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Core.Domain.Workflow;
using Apperture.IntegrationTests.Audit;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Apperture.IntegrationTests;

/// <summary>
/// Real SQL Server proof for LFC-1.1 schedule hard-delete. InMemory cannot take UPDLOCK or hold an
/// application lock; these tests drive the production repository and schedule service against a
/// throwaway catalog. Fail hard when SQL Server is unreachable — a skip is not evidence.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ScheduleDeleteSqlServerTests
{
    private const int LockTimeoutNumber = 1222;

    [Fact]
    public async Task Disabled_schedule_graph_delete_removes_live_rows_and_keeps_unrelated()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11A_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db);

        var target = await CreateDisabledAsync(env, "Nightly");
        var other = await CreateDisabledAsync(env, "Weekly");
        var targetAssignment = await env.Schedules.AssignToRuntimePlaneAsync(target.Id, new RuntimePlaneId("local"), null);
        var otherAssignment = await env.Schedules.AssignToRuntimePlaneAsync(other.Id, new RuntimePlaneId("local"), null);
        await WriteStateAsync(db, targetAssignment.Id.Value, "Succeeded");
        await WriteStateAsync(db, otherAssignment.Id.Value, "Succeeded");

        await env.Schedules.DeleteScheduleAsync(target.Id);

        (await db.ScheduleDefinitions.AsNoTracking().AnyAsync(s => s.Id == target.Id.Value)).Should().BeFalse();
        (await db.ScheduleAssignments.AsNoTracking().AnyAsync(a => a.ScheduleId == target.Id.Value)).Should().BeFalse();
        (await db.ScheduleStates.AsNoTracking().AnyAsync(s => s.AssignmentId == targetAssignment.Id.Value)).Should().BeFalse();
        (await db.ScheduleDefinitions.AsNoTracking().SingleAsync(s => s.Id == other.Id.Value)).Name.Should().Be("Weekly");
        (await db.ScheduleAssignments.AsNoTracking().CountAsync(a => a.ScheduleId == other.Id.Value)).Should().Be(1);
        (await db.ScheduleStates.AsNoTracking().AnyAsync(s => s.AssignmentId == otherAssignment.Id.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task Enabled_schedule_delete_is_refused_and_graph_remains()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11B_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db);

        var schedule = await CreateDisabledAsync(env, "Nightly");
        await env.Schedules.EnableAsync(schedule.Id);
        var assignment = await env.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);
        await WriteStateAsync(db, assignment.Id.Value, "Succeeded");

        var act = async () => await env.Schedules.DeleteScheduleAsync(schedule.Id);

        await act.Should().ThrowAsync<ScheduleDeleteRejectedException>();
        var row = await db.ScheduleDefinitions.AsNoTracking().SingleAsync(s => s.Id == schedule.Id.Value);
        row.Enabled.Should().BeTrue();
        (await db.ScheduleAssignments.AsNoTracking().CountAsync(a => a.ScheduleId == schedule.Id.Value)).Should().Be(1);
        (await db.ScheduleStates.AsNoTracking().AnyAsync(s => s.AssignmentId == assignment.Id.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task ScheduleRun_and_idempotency_row_survive_delete_and_a_later_claim_is_still_history()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11C_");
        await using var db = fixture.NewDb();
        var env = Env.Build(db);

        var schedule = await CreateDisabledAsync(env, "Nightly");
        var assignment = await env.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);
        var fire = new DateTimeOffset(2026, 1, 1, 3, 15, 0, TimeSpan.Zero);
        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = schedule.Id.Value,
            AssignmentId = assignment.Id.Value,
            FireTimeUtc = fire,
            StartedAtUtc = fire,
            Outcome = "Succeeded",
            RunId = "run-1",
            ResourceContextId = ResourceContextIds.SharedValue
        });
        var idempotencyKey = $"schedule:{schedule.Id.Value}:{fire:O}";
        db.OperationStartRequests.Add(new OperationStartRequestEntity
        {
            RunId = "run-1",
            OperationInstanceId = "op-1",
            OperationDefinitionId = "proc-1",
            IdempotencyKey = idempotencyKey,
            Status = WorkflowLifecycleStatusNames.Requested,
            CreatedAt = fire,
            ResourceContextId = ResourceContextIds.SharedValue
        });
        await db.SaveChangesAsync();

        await env.Schedules.DeleteScheduleAsync(schedule.Id);

        var run = await db.ScheduleRuns.AsNoTracking().SingleAsync();
        run.ScheduleId.Should().Be(schedule.Id.Value);
        run.RunId.Should().Be("run-1");
        var start = await db.OperationStartRequests.AsNoTracking().SingleAsync();
        start.IdempotencyKey.Should().Be(idempotencyKey);

        // A claim that already left due-evaluation can still insert a ScheduleRun: there is no FK from
        // ScheduleRuns onto the definition. That row is retained history, not live schedule state.
        var claimed = await env.Repository.TryClaimFireAsync(
            schedule.Id.Value, fire.AddHours(1), assignment.Id.Value, "worker-local");
        claimed.Should().BeTrue();
        (await db.ScheduleRuns.AsNoTracking().CountAsync(r => r.ScheduleId == schedule.Id.Value)).Should().Be(2);
        (await db.ScheduleDefinitions.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await db.ScheduleAssignments.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await db.ScheduleStates.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Enable_that_holds_the_gate_keeps_delete_busy_then_delete_observes_enabled()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11D1_");
        var scheduleId = await SeedDisabledScheduleAsync(fixture, "Nightly");

        await using var holder = fixture.NewDb();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        await new SqlResourceOwnershipMutationGate(holder).AcquireAsync();
        var holderRepo = new SqlScheduleRepository(holder);
        var fresh = await holderRepo.GetDefinitionAsync(new ScheduleId(scheduleId));
        await holderRepo.UpdateDefinitionAsync(fresh! with { Enabled = true });

        await using var waiter = fixture.NewDb();
        var busyDelete = Env.Build(waiter, gateTimeout: TimeSpan.Zero);
        var busy = async () => await busyDelete.Schedules.DeleteScheduleAsync(new ScheduleId(scheduleId));
        await busy.Should().ThrowAsync<ResourceOwnershipBusyException>();

        await holderTx.CommitAsync();

        await using var after = fixture.NewDb();
        var act = async () => await Env.Build(after).Schedules.DeleteScheduleAsync(new ScheduleId(scheduleId));
        await act.Should().ThrowAsync<ScheduleDeleteRejectedException>();
        (await after.ScheduleDefinitions.AsNoTracking().SingleAsync()).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_that_holds_the_gate_keeps_enable_busy_then_enable_cannot_resurrect()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11D2_");
        var scheduleId = await SeedDisabledScheduleAsync(fixture, "Nightly");

        await using var holder = fixture.NewDb();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        await new SqlResourceOwnershipMutationGate(holder).AcquireAsync();
        var holderRepo = new SqlScheduleRepository(holder);
        _ = await holderRepo.GetByIdForUpdateAsync(new ScheduleId(scheduleId));
        var removed = await holderRepo.DeleteDisabledScheduleGraphAsync(new ScheduleId(scheduleId));
        removed.Deleted.Should().BeTrue();

        await using var waiter = fixture.NewDb();
        var busyEnable = Env.Build(waiter, gateTimeout: TimeSpan.Zero);
        var busy = async () => await busyEnable.Schedules.EnableAsync(new ScheduleId(scheduleId));
        await busy.Should().ThrowAsync<ResourceOwnershipBusyException>();

        await holderTx.CommitAsync();

        await using var after = fixture.NewDb();
        var act = async () => await Env.Build(after).Schedules.EnableAsync(new ScheduleId(scheduleId));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        (await after.ScheduleDefinitions.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Assignment_insert_blocks_on_the_definition_row_lock_then_fails_after_delete()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11E_");
        var scheduleId = await SeedDisabledScheduleAsync(fixture, "Nightly");

        await using var holder = fixture.NewDb();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        var holderRepo = new SqlScheduleRepository(holder);
        _ = await holderRepo.GetByIdForUpdateAsync(new ScheduleId(scheduleId));

        await using var waiter = fixture.NewDb();
        await waiter.Database.OpenConnectionAsync();
        await waiter.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");
        var assign = async () => await Env.Build(waiter).Schedules.AssignToRuntimePlaneAsync(
            new ScheduleId(scheduleId), new RuntimePlaneId("local"), null);
        await assign.Should().ThrowAsync<Exception>().Where(ex => IsLockTimeout(ex));

        (await holderRepo.DeleteDisabledScheduleGraphAsync(new ScheduleId(scheduleId))).Deleted.Should().BeTrue();
        await holderTx.CommitAsync();

        await using var after = fixture.NewDb();
        var afterAssign = async () => await Env.Build(after).Schedules.AssignToRuntimePlaneAsync(
            new ScheduleId(scheduleId), new RuntimePlaneId("local"), null);
        await afterAssign.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
        (await after.ScheduleAssignments.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task State_update_blocks_on_the_assignment_row_lock_then_does_not_recreate_after_delete()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11F_");
        string scheduleId;
        string assignmentId;
        await using (var setup = fixture.NewDb())
        {
            var env = Env.Build(setup);
            var schedule = await CreateDisabledAsync(env, "Nightly");
            var assignment = await env.Schedules.AssignToRuntimePlaneAsync(schedule.Id, new RuntimePlaneId("local"), null);
            await WriteStateAsync(setup, assignment.Id.Value, "Succeeded");
            scheduleId = schedule.Id.Value;
            assignmentId = assignment.Id.Value;
        }

        await using var holder = fixture.NewDb();
        await using var holderTx = await holder.Database.BeginTransactionAsync();
        var locked = await holder.ScheduleAssignments
            .FromSqlInterpolated($"SELECT * FROM [ScheduleAssignments] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {assignmentId}")
            .AsNoTracking()
            .ToListAsync();
        locked.Should().ContainSingle();

        await using var waiter = fixture.NewDb();
        await waiter.Database.OpenConnectionAsync();
        await waiter.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");
        var update = async () => await new SqlScheduleRepository(waiter).UpdateStateAsync(
            assignmentId, new ScheduleStateUpdate(null, DateTimeOffset.UtcNow, "Dispatched", null, null));
        await update.Should().ThrowAsync<Exception>().Where(ex => IsLockTimeout(ex));

        var holderRepo = new SqlScheduleRepository(holder);
        (await holderRepo.DeleteDisabledScheduleGraphAsync(new ScheduleId(scheduleId))).Deleted.Should().BeTrue();
        await holderTx.CommitAsync();

        await using var after = fixture.NewDb();
        await new SqlScheduleRepository(after).UpdateStateAsync(
            assignmentId, new ScheduleStateUpdate(null, DateTimeOffset.UtcNow, "Dispatched", null, null));
        (await after.ScheduleStates.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await after.ScheduleAssignments.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Schedule_graph_has_no_fk_that_would_cascade_or_block_definition_delete()
    {
        await using var fixture = await RequireFixtureAsync("Apperture_Lfc11Fk_");
        await using var db = fixture.NewDb();
        await db.Database.OpenConnectionAsync();

        var rows = new List<(string Name, string Parent, string Referenced, string DeleteAction)>();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT fk.name, OBJECT_NAME(fk.parent_object_id), OBJECT_NAME(fk.referenced_object_id),
                       fk.delete_referential_action_desc
                FROM sys.foreign_keys fk
                WHERE OBJECT_NAME(fk.parent_object_id) IN (N'ScheduleDefinitions', N'ScheduleAssignments', N'ScheduleStates', N'ScheduleRuns')
                   OR OBJECT_NAME(fk.referenced_object_id) IN (N'ScheduleDefinitions', N'ScheduleAssignments', N'ScheduleStates', N'ScheduleRuns');
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        rows.Should().ContainSingle(r =>
            r.Parent == "ScheduleDefinitions" && r.Referenced == "ResourceContexts" && r.DeleteAction == "NO_ACTION");
        rows.Should().NotContain(r => r.Parent == "ScheduleAssignments");
        rows.Should().NotContain(r => r.Parent == "ScheduleStates");
        rows.Should().NotContain(r => r.Parent == "ScheduleRuns");
        rows.Should().NotContain(r => r.Referenced == "ScheduleDefinitions" && r.Parent != "ScheduleDefinitions");
    }

    /// <summary>The signed-in administrator of every delete here. The audited unit records and verifies their row.</summary>
    private const string ActorId = "usr_lfc11_sql_actor";

    /// <summary>A scratch catalog with the acting user in it. Fails, never skips, when SQL Server is unreachable.</summary>
    private static async Task<SqlServerScratchDatabase> RequireFixtureAsync(string catalogPrefix)
    {
        var fixture = await SqlServerScratchDatabase.CreateAsync(catalogPrefix);
        fixture.Should().NotBeNull(
            "LFC-1.1 SQL proof requires these tests to run against SQL Server. " +
            SqlServerTestConfiguration.DescribeActiveConfiguration());

        // The audited unit attributes the delete to this user, so the account must exist.
        await using var db = fixture!.NewDb();
        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO Users (Id, Username, DisplayName, IsBreakGlassEligible) VALUES (N'{ActorId}', N'lfc11.sql.actor', N'{ActorId}', 0);
            INSERT INTO UsernameLedger (Username, UserId, AssignedAtUtc) VALUES (N'lfc11.sql.actor', N'{ActorId}', SYSDATETIMEOFFSET());
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, LinkedAt) VALUES (N'L{ActorId}', N'{ActorId}', 1, N'lfc11.sql.actor', SYSDATETIMEOFFSET());
            """);
        return fixture;
    }

    private static async Task<string> SeedDisabledScheduleAsync(SqlServerScratchDatabase fixture, string name)
    {
        await using var db = fixture.NewDb();
        var schedule = await CreateDisabledAsync(Env.Build(db), name);
        return schedule.Id.Value;
    }

    private static async Task<ScheduleDefinition> CreateDisabledAsync(Env env, string name) =>
        await env.Schedules.CreateScheduleAsync(new ScheduleDefinition(
            new ScheduleId("sch-" + Guid.NewGuid().ToString("N")[..12]),
            name,
            "0 0 * * *",
            "UTC",
            Enabled: false,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{}"),
            ExecutionPolicyRef: null,
            Version: 1));

    private static async Task WriteStateAsync(AppertureDbContext db, string assignmentId, string outcome)
    {
        db.ScheduleStates.Add(new ScheduleStateEntity
        {
            AssignmentId = assignmentId,
            LastOutcome = outcome,
            LastAttemptedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        });
        await db.SaveChangesAsync();
    }

    private static bool IsLockTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == LockTimeoutNumber)
                return true;
        }

        return false;
    }

    private sealed class Env
    {
        public required IScheduleManagementService Schedules { get; init; }
        public required SqlScheduleRepository Repository { get; init; }

        public static Env Build(AppertureDbContext db, TimeSpan? gateTimeout = null)
        {
            var repository = new SqlScheduleRepository(db);
            var principal = new AuditRecorderTestSupport.Principal(ActorId);
            var schedules = new SqlScheduleManagementService(
                repository,
                new StubClaimOptions(),
                TimeProvider.System,
                new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
                new SqlIntegrationRepository(db),
                db,
                new SqlResourceOwnershipMutationGate(db, gateTimeout),
                principal,
                AuditRecorderTestSupport.NewUnit(db, principal));
            return new Env { Schedules = schedules, Repository = repository };
        }
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }
}
