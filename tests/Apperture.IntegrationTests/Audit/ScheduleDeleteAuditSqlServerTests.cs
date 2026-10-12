using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 3: the schedule hard-delete against SQL Server (design gate §13.4 to §13.6, M6, M7). The removal of the
/// schedule's graph and its <c>schedule.deleted</c> row commit together or not at all, and every way a commit can be uncertain
/// is verified from the committed primary row, never from the schedule's absence. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ScheduleDeleteAuditSqlServerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public ScheduleDeleteAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud14Sched_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- The committed delete ----

    [Fact]
    public async Task A_delete_removes_the_graph_and_commits_exactly_one_primary_row_attributed_to_the_administrator()
    {
        var (admin, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");
        await using (var db = _db.NewDb())
        {
            db.ScheduleRuns.Add(new ScheduleRunEntity
            {
                ScheduleId = seeded.ScheduleId,
                AssignmentId = seeded.AssignmentId,
                FireTimeUtc = DateTimeOffset.Parse("2026-01-01T03:15:00Z"),
                StartedAtUtc = DateTimeOffset.Parse("2026-01-01T03:15:00Z"),
                Outcome = "Succeeded",
                RunId = "run-1",
                ResourceContextId = ResourceContextIds.SharedValue,
            });
            await db.SaveChangesAsync();
        }

        await DeleteAsync(principal, seeded.ScheduleId);

        (await GraphAsync(seeded)).Should().Be(new Graph(0, 0, 0));
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM ScheduleRuns WHERE ScheduleId = N'{seeded.ScheduleId}';"))
            .Should().Be(1, "the dangling schedule id in history is preserved");

        var row = (await AuditRowsAsync()).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleDeleted);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["TargetType"].Should().Be(AuditTargetTypes.Schedule);
        row["TargetId"].Should().Be(seeded.ScheduleId);
        row["ScheduleId"].Should().Be(seeded.ScheduleId);
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["OperationEventId"].Should().Be(row["EventId"], "it is the primary row of its own operation");
        Detail(row, "name").Should().Be("Nightly");
        Detail(row, "assignmentsRemoved").Should().Be("1");
        Detail(row, "integrationId").Should().BeNull();
        ((string)row["Details"]!).Should().NotContain("0 0 * * *", "the recurrence is never recorded");
    }

    [Fact]
    public async Task The_primary_row_carries_the_operation_event_id_that_verification_correlates_on()
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");
        var operationId = Guid.NewGuid().ToString("N");

        await using (var db = _db.NewDb())
        {
            await Schedules(db, principal, NewUnit(db, principal, eventIds: new FixedEventIds(operationId))).DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId));
        }

        var row = (await AuditRowsAsync()).Should().ContainSingle().Subject;
        row["EventId"].Should().Be(operationId);
        row["OperationEventId"].Should().Be(operationId);
    }

    // ---- Refusals ----

    [Fact]
    public async Task An_enabled_schedule_is_refused_with_one_rejection_row_and_nothing_is_removed()
    {
        var (admin, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly", enabled: true);

        var act = () => DeleteAsync(principal, seeded.ScheduleId);

        await act.Should().ThrowAsync<ScheduleDeleteRejectedException>();
        (await GraphAsync(seeded)).Should().Be(new Graph(1, 1, 1));
        var row = (await AuditRowsAsync()).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleDeleteRejected);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["TargetType"].Should().Be(AuditTargetTypes.Schedule);
        row["TargetId"].Should().Be(seeded.ScheduleId);
        row["ScheduleId"].Should().Be(seeded.ScheduleId);
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Rejected);
        Detail(row, "reason").Should().Be("enabled");
        Detail(row, "name").Should().Be("Nightly");
    }

    [Fact]
    public async Task A_rejection_row_that_cannot_be_written_does_not_change_the_refusal()
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly", enabled: true);
        var failWriter = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using var db = _db.NewDb();
        var schedules = Schedules(db, principal, NewUnit(db, principal), new SqlAuditEventWriter(_db.Options(failWriter), principal, HarnessCatalog, null, null, null));
        var act = () => schedules.DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId)).AsTask();

        await act.Should().ThrowAsync<ScheduleDeleteRejectedException>("a failed W3 write never changes the legitimate refusal");
        failWriter.Failures.Should().BeGreaterThan(0, "the rejection row was attempted");
        (await AuditRowsAsync()).Should().BeEmpty();
        (await GraphAsync(seeded)).Should().Be(new Graph(1, 1, 1));
    }

    [Fact]
    public async Task A_missing_schedule_is_a_typed_not_found_that_records_nothing()
    {
        var (_, principal) = await AdminAsync();

        var act = () => DeleteAsync(principal, "sch-missing");

        await act.Should().ThrowAsync<ScheduleNotFoundException>();
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    // ---- Atomicity ----

    [Fact]
    public async Task When_the_audit_row_cannot_be_inserted_the_whole_delete_rolls_back()
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => Schedules(db, principal, NewUnit(db, principal)).DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId)).AsTask();
            // The injected failure itself, as the save reports it, not any exception: a typed not-found or refusal is not this.
            (await act.Should().ThrowAsync<DbUpdateException>())
                .Which.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().StartWith("Injected:");
            db.ChangeTracker.Entries().Should().BeEmpty("nothing the failed attempt tracked is left to save");
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await GraphAsync(seeded)).Should().Be(new Graph(1, 1, 1), "definition, assignment and cursor are all still there");
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    // ---- Ambiguous commits (M6) ----

    [Fact]
    public async Task A_lost_acknowledgement_is_verified_and_reported_as_the_successful_delete_though_the_schedule_is_gone()
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(lost, attempts))
        {
            var act = () => Schedules(db, principal, NewUnit(db, principal)).DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId)).AsTask();

            await act.Should().NotThrowAsync("the commit was verified, so the delete is a success, never a missing target");
        }

        attempts.Started.Should().Be(1, "a verified commit is not run again, so it cannot find the schedule missing");
        (await GraphAsync(seeded)).Should().Be(new Graph(0, 0, 0));
        (await AuditRowsAsync()).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.ScheduleDeleted);
    }

    [Fact]
    public async Task A_confirmed_rollback_reruns_the_delete_once_and_it_succeeds_with_one_row()
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");
        var rolledBack = new RolledBackCommit();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(rolledBack, attempts))
        {
            await Schedules(db, principal, NewUnit(db, principal)).DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId));
        }

        attempts.Started.Should().Be(2);
        (await GraphAsync(seeded)).Should().Be(new Graph(0, 0, 0));
        (await AuditRowsAsync()).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.ScheduleDeleted, "the rolled-back attempt left no row");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_outcome_leaves_the_delete_wholly_committed_or_wholly_absent(bool committed)
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");
        DbTransactionInterceptor fault = committed ? new LostCommitAcknowledgement() : new RolledBackCommit();

        await using (var db = _db.NewDb(fault))
        {
            var unit = new SqlAuditedUnitOfWork(db, principal, HarnessCatalog, TimeProvider.System, FastOptions(), null, null, new UnreachableVerification());
            var act = () => Schedules(db, principal, unit).DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId)).AsTask();

            await act.Should().ThrowAsync<AuditOutcomeUnknownException>("verification could not establish the outcome");
        }

        var rows = await AuditRowsAsync();
        if (committed)
        {
            (await GraphAsync(seeded)).Should().Be(new Graph(0, 0, 0));
            rows.Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.ScheduleDeleted);
        }
        else
        {
            (await GraphAsync(seeded)).Should().Be(new Graph(1, 1, 1));
            rows.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task When_the_operation_lock_is_held_by_another_session_the_outcome_is_unknown_and_nothing_changes()
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(_db.ConnectionString, "apperture.audit-op." + operationId);

        await using var db = _db.NewDb();
        var act = () => Schedules(db, principal, NewUnit(db, principal, eventIds: new FixedEventIds(operationId)))
            .DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId)).AsTask();

        (await act.Should().ThrowAsync<AuditOutcomeUnknownException>()).Which.OperationEventId.Should().Be(operationId);
        (await GraphAsync(seeded)).Should().Be(new Graph(1, 1, 1));
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Two_independent_deletes_of_the_same_schedule_commit_one_row_and_the_loser_is_a_genuine_not_found()
    {
        var (_, principal) = await AdminAsync();
        var seeded = await SeedAsync(principal, "Nightly");

        async Task<Exception?> Attempt()
        {
            await using var db = _db.NewDb();
            try
            {
                await Schedules(db, principal, NewUnit(db, principal)).DeleteScheduleAsync(new ScheduleId(seeded.ScheduleId));
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(Attempt(), Attempt());

        results.Count(r => r is null).Should().Be(1);
        results.Single(r => r is not null).Should().BeOfType<ScheduleNotFoundException>("it waited for the winner, then found nothing");
        (await GraphAsync(seeded)).Should().Be(new Graph(0, 0, 0));
        (await AuditRowsAsync()).Should().ContainSingle(r => (string)r["Action"]! == AuditActionNames.ScheduleDeleted, "exactly one primary row");
    }

    // ---- wiring ----

    private SqlScheduleManagementService Schedules(
        AppertureDbContext db,
        IPrincipalContext principal,
        IAuditedUnitOfWork? unit = null,
        IAuditEventWriter? events = null,
        TimeSpan? gateTimeout = null) =>
        new(
            new SqlScheduleRepository(db),
            new StubClaimOptions(),
            TimeProvider.System,
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            new SqlIntegrationRepository(db),
            db,
            new SqlResourceOwnershipMutationGate(db, gateTimeout),
            principal,
            unit,
            events ?? new SqlAuditEventWriter(_db.Options(), principal, HarnessCatalog, null, null, null));

    private async Task DeleteAsync(IPrincipalContext principal, string scheduleId)
    {
        await using var db = _db.NewDb();
        await Schedules(db, principal, NewUnit(db, principal)).DeleteScheduleAsync(new ScheduleId(scheduleId));
    }

    private sealed record Seeded(string ScheduleId, string AssignmentId);

    private sealed record Graph(int Definitions, int Assignments, int States);

    /// <summary>
    /// A schedule with one assignment and one cursor, inserted directly: the delete suite asserts the audit rows of the delete
    /// alone, so the set-up must not write rows of its own.
    /// </summary>
    private async Task<Seeded> SeedAsync(IPrincipalContext principal, string name, bool enabled = false)
    {
        _ = principal;
        var scheduleId = "sch-" + Guid.NewGuid().ToString("N")[..12];
        var assignmentId = Guid.NewGuid().ToString("N")[..24];
        await using var db = _db.NewDb();
        db.ScheduleDefinitions.Add(new ScheduleDefinitionEntity
        {
            Id = scheduleId,
            ResourceContextId = ResourceContextIds.SharedValue,
            Name = name,
            RecurrenceExpression = "0 0 * * *",
            TimeZone = "UTC",
            Enabled = enabled,
            TargetJobType = (int)ScheduledJobType.WorkflowExecution,
            TargetJobConfigurationJson = "{}",
            Version = 1,
        });
        db.ScheduleAssignments.Add(new ScheduleAssignmentEntity
        {
            Id = assignmentId,
            ScheduleId = scheduleId,
            RuntimePlaneId = "local",
            DesiredVersion = 1,
            AppliedVersion = 0,
        });
        db.ScheduleStates.Add(new ScheduleStateEntity
        {
            AssignmentId = assignmentId,
            LastOutcome = "Succeeded",
            LastAttemptedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        });
        await db.SaveChangesAsync();
        return new Seeded(scheduleId, assignmentId);
    }

    private async Task<Graph> GraphAsync(Seeded seeded) =>
        new(
            await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM ScheduleDefinitions WHERE Id = N'{seeded.ScheduleId}';"),
            await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM ScheduleAssignments WHERE ScheduleId = N'{seeded.ScheduleId}';"),
            await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM ScheduleStates WHERE AssignmentId = N'{seeded.AssignmentId}';"));

    private async Task<(string Admin, Principal Principal)> AdminAsync()
    {
        var id = NewUserId();
        await _db.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    private Task<List<Dictionary<string, object?>>> AuditRowsAsync() =>
        _db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

    private static string? Detail(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        if (!details.RootElement.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }
}
