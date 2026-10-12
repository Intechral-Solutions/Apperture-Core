using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 4: schedule create (with its initial assignment), update, enable, disable and standalone assignment against SQL
/// Server (design gate §13.4, §13.5, M6, M7). Each is one audited unit: the change and its one primary row commit together or
/// not at all, and a create can no longer leave a schedule without its assignment. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class ScheduleLifecycleAuditSqlServerTests : IAsyncLifetime
{
    private const string Recurrence = "17 4 * * *";
    private const string TargetJson = "{\"parameter\":\"sentinel-target\"}";

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public ScheduleLifecycleAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public enum Op
    {
        CreateWithAssignment,
        CreateWithoutAssignment,
        Rename,
        Retime,
        Enable,
        Disable,
        EnableByUpdate,
        Assign,
        PatchRename,
        PatchEnable,
    }

    public static IEnumerable<object[]> Ops() => Enum.GetValues<Op>().Select(op => new object[] { op });

    public static IEnumerable<object[]> OpsWithOutcome() =>
        from op in Enum.GetValues<Op>()
        from committed in new[] { true, false }
        select new object[] { op, committed };

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud14Schedule_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- M7: the committed operation ----

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_committed_operation_records_exactly_one_primary_row_attributed_to_the_administrator(Op op)
    {
        var (admin, principal) = await AdminAsync();
        var arranged = await ArrangeAsync(op, principal);
        var before = await SnapshotAsync();

        await using (var db = _db.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync()).Should().NotBe(before);
        var row = (await AuditRowsAsync()).Should().ContainSingle().Subject;
        row["Action"].Should().Be(arranged.Action);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        row["TargetType"].Should().Be(AuditTargetTypes.Schedule);
        var scheduleId = arranged.ScheduleId ?? await _db.ScalarAsync<string>("SELECT TOP 1 Id FROM ScheduleDefinitions;");
        row["TargetId"].Should().Be(scheduleId);
        row["ScheduleId"].Should().Be(scheduleId);
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["OperationEventId"].Should().Be(row["EventId"], "it is the primary row of its own operation");
        arranged.AssertDetails(row);

        var details = (string)row["Details"]!;
        details.Should().NotContain(Recurrence, "the recurrence is never recorded").And.NotContain("sentinel", "no target, policy or override content is recorded");
    }

    [Fact]
    public async Task A_create_cannot_leave_a_schedule_without_its_assignment()
    {
        var (_, principal) = await AdminAsync();
        var failAssignment = new FailingCommand(sql => sql.Contains("INSERT INTO [ScheduleAssignments]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAssignment))
        {
            var act = () => Service(db, principal, NewUnit(db, principal)).CreateScheduleWithAssignmentAsync(
                NewDefinition(null), new RuntimePlaneId("local"), null).AsTask();
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        failAssignment.Failures.Should().BeGreaterThan(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM ScheduleDefinitions;")).Should().Be(0, "the definition rolled back with the failed assignment");
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Requests_that_change_nothing_write_nothing_and_record_nothing()
    {
        var (_, principal) = await AdminAsync();
        var id = await SeedScheduleAsync(enabled: true);
        var before = await _db.RowsAsync($"SELECT * FROM ScheduleDefinitions WHERE Id = N'{id}';");

        await using (var db = _db.NewDb())
        {
            var service = Service(db, principal, NewUnit(db, principal));
            await service.EnableAsync(new ScheduleId(id));
            var current = (await service.GetDefinitionAsync(new ScheduleId(id)))!;
            await service.UpdateScheduleAsync(current);
        }

        (await _db.RowsAsync($"SELECT * FROM ScheduleDefinitions WHERE Id = N'{id}';")).Should().BeEquivalentTo(before, "the lifecycle timestamps did not move");
        (await AuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_that_enables_and_renames_records_one_enabled_row_naming_the_other_change()
    {
        var (_, principal) = await AdminAsync();
        var id = await SeedScheduleAsync(enabled: false);

        await using (var db = _db.NewDb())
        {
            var service = Service(db, principal, NewUnit(db, principal));
            var current = (await service.GetDefinitionAsync(new ScheduleId(id)))!;
            await service.UpdateScheduleAsync(current with { Name = "Renamed", Enabled = true });
        }

        var row = (await AuditRowsAsync()).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleEnabled);
        Changed(row).Should().Equal("name");
        (await _db.ScalarAsync<string>($"SELECT Name FROM ScheduleDefinitions WHERE Id = N'{id}';")).Should().Be("Renamed");
    }

    // ---- Waiting on the schedule row (the lock a delete holds) ----

    [Theory]
    [InlineData(Op.Rename)]
    [InlineData(Op.Enable)]
    [InlineData(Op.Disable)]
    [InlineData(Op.Assign)]
    [InlineData(Op.PatchRename)]
    public async Task A_change_that_waits_for_a_delete_in_progress_finds_the_schedule_gone_and_records_nothing(Op op)
    {
        var (_, principal) = await AdminAsync();
        var arranged = await ArrangeAsync(op, principal);

        await using var deleting = await LockedRowHolder.TakeAsync(_db.ConnectionString, "ScheduleDefinitions", arranged.ScheduleId!);
        await using var db = _db.NewDb();
        var writer = Task.Run(() => arranged.Run(db, NewUnit(db, principal)));
        await deleting.WaitUntilBlockedAsync(_db, "ScheduleDefinitions", writer);

        await deleting.ExecuteAsync(
            "DELETE FROM ScheduleAssignments WHERE ScheduleId = @id; DELETE FROM ScheduleDefinitions WHERE Id = @id;", ("@id", arranged.ScheduleId!));
        await deleting.CommitAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => writer);
        thrown.Should().BeOfType<ScheduleNotFoundException>("a schedule removed while the change waited is a plain refusal, never an audit invariant");
        (await AuditRowsAsync()).Should().BeEmpty();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM ScheduleAssignments;")).Should().Be(0, "no assignment is added for a removed schedule");
    }

    // ---- M6 ----

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_no_partial_change(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once_with_one_row(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OpsWithOutcome))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [InlineData(Op.CreateWithAssignment)]
    [InlineData(Op.Enable)]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        var (_, principal) = await AdminAsync();
        await (await RunnerAsync(op, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- wiring ----

    private SqlScheduleManagementService Service(AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit) =>
        new(
            new SqlScheduleRepository(db),
            new StubClaimOptions(),
            TimeProvider.System,
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            new SqlIntegrationRepository(db),
            db,
            new SqlResourceOwnershipMutationGate(db),
            principal,
            unit);

    private static ScheduleDefinition NewDefinition(IntegrationId? integrationId, bool enabled = false) =>
        new(
            new ScheduleId("sch-" + Guid.NewGuid().ToString("N")[..12]),
            "Nightly import",
            Recurrence,
            "UTC",
            enabled,
            new ScheduledTarget(ScheduledJobType.WorkflowExecution, "{\"parameter\":\"sentinel-target\"}"),
            "policy-ref-sentinel",
            Version: 1,
            integrationId);

    private sealed record Arranged(
        string Action,
        string? ScheduleId,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        Action<Dictionary<string, object?>> AssertDetails);

    private async Task<Arranged> ArrangeAsync(Op op, IPrincipalContext principal)
    {
        switch (op)
        {
            case Op.CreateWithAssignment:
            {
                var integrationId = await SeedIntegrationAsync();
                return new Arranged(
                    AuditActionNames.ScheduleCreated,
                    null,
                    (db, unit) => Service(db, principal, unit)
                        .CreateScheduleWithAssignmentAsync(NewDefinition(new IntegrationId(integrationId)), new RuntimePlaneId("local"), null).AsTask(),
                    row =>
                    {
                        Detail(row, "integrationId").Should().Be(integrationId);
                        Detail(row, "assignmentsCreated").Should().Be("1");
                        Detail(row, "runtimePlaneId").Should().Be("local");
                        Detail(row, "name").Should().Be("Nightly import");
                    });
            }

            case Op.CreateWithoutAssignment:
                return new Arranged(
                    AuditActionNames.ScheduleCreated,
                    null,
                    (db, unit) => Service(db, principal, unit).CreateScheduleAsync(NewDefinition(null)).AsTask(),
                    row =>
                    {
                        Detail(row, "assignmentsCreated").Should().Be("0");
                        Detail(row, "integrationId").Should().BeNull();
                    });
            case Op.Rename:
                return await ChangeAsync(principal, enabled: false, AuditActionNames.ScheduleUpdated, s => s with { Name = "Renamed" }, row => Changed(row).Should().Equal("name"));
            case Op.Retime:
                return await ChangeAsync(
                    principal,
                    enabled: false,
                    AuditActionNames.ScheduleUpdated,
                    s => s with { RecurrenceExpression = "5 5 * * *", TimeZone = "Europe/London" },
                    row => Changed(row).Should().Equal("recurrence", "timeZone"));
            case Op.Enable:
            {
                var id = await SeedScheduleAsync(enabled: false);
                return new Arranged(
                    AuditActionNames.ScheduleEnabled,
                    id,
                    (db, unit) => Service(db, principal, unit).EnableAsync(new ScheduleId(id)).AsTask(),
                    row => Changed(row).Should().BeEmpty());
            }

            case Op.Disable:
            {
                var id = await SeedScheduleAsync(enabled: true);
                return new Arranged(
                    AuditActionNames.ScheduleDisabled,
                    id,
                    (db, unit) => Service(db, principal, unit).DisableAsync(new ScheduleId(id)).AsTask(),
                    row => Changed(row).Should().BeEmpty());
            }

            case Op.EnableByUpdate:
                return await ChangeAsync(principal, enabled: false, AuditActionNames.ScheduleEnabled, s => s with { Enabled = true }, row => Changed(row).Should().BeEmpty());
            case Op.PatchRename:
                return await PatchAsync(principal, enabled: true, AuditActionNames.ScheduleUpdated, new SchedulePatch(Name: "Renamed"), row => Changed(row).Should().Equal("name"));
            case Op.PatchEnable:
                return await PatchAsync(principal, enabled: false, AuditActionNames.ScheduleEnabled, new SchedulePatch(Enabled: true), row => Changed(row).Should().BeEmpty());
            default:
            {
                var id = await SeedScheduleAsync(enabled: false);
                return new Arranged(
                    AuditActionNames.ScheduleAssigned,
                    id,
                    (db, unit) => Service(db, principal, unit)
                        .AssignToRuntimePlaneAsync(new ScheduleId(id), new RuntimePlaneId("plane-2"), "{\"x\":\"sentinel-overrides\"}").AsTask(),
                    row =>
                    {
                        Detail(row, "runtimePlaneId").Should().Be("plane-2");
                        Detail(row, "policyOverridesSet").Should().Be("true");
                    });
            }
        }
    }

    private async Task<Arranged> ChangeAsync(
        IPrincipalContext principal, bool enabled, string action, Func<ScheduleDefinition, ScheduleDefinition> change, Action<Dictionary<string, object?>> assert)
    {
        var id = await SeedScheduleAsync(enabled);
        return new Arranged(
            action,
            id,
            async (db, unit) =>
            {
                var service = Service(db, principal, unit);
                var current = (await service.GetDefinitionAsync(new ScheduleId(id)))!;
                await service.UpdateScheduleAsync(change(current));
            },
            assert);
    }

    private async Task<Arranged> PatchAsync(
        IPrincipalContext principal, bool enabled, string action, SchedulePatch patch, Action<Dictionary<string, object?>> assert)
    {
        var id = await SeedScheduleAsync(enabled);
        return new Arranged(
            action,
            id,
            async (db, unit) =>
            {
                var committed = await Service(db, principal, unit).PatchScheduleAsync(new ScheduleId(id), patch);
                // Built from the attempt's write, or read back after a verified commit: either way the committed schedule.
                committed.Should().BeEquivalentTo(patch.ApplyTo(committed));
                committed.Id.Value.Should().Be(id);
            },
            assert);
    }

    private async Task<AuditedOperationM6Runner> RunnerAsync(Op op, IPrincipalContext principal)
    {
        var arranged = await ArrangeAsync(op, principal);
        return new AuditedOperationM6Runner(_db, SnapshotAsync, arranged.Action, arranged.Run, "sentinel");
    }

    private async Task<string> SnapshotAsync()
    {
        var definitions = await _db.RowsAsync(
            "SELECT Id, IntegrationId, Name, RecurrenceExpression, TimeZone, Enabled, ResourceContextId, LastScheduleEnabledAtUtc, LastScheduleDisabledAtUtc FROM ScheduleDefinitions ORDER BY Id;");
        var assignments = await _db.RowsAsync("SELECT ScheduleId, RuntimePlaneId, PolicyOverrides FROM ScheduleAssignments ORDER BY ScheduleId, RuntimePlaneId;");
        return string.Join("|", definitions.Select(r => string.Join(",", r.Values.Select(v => v?.ToString()))))
            + "#" + string.Join("|", assignments.Select(r => string.Join(",", r.Values.Select(v => v?.ToString()))));
    }

    private async Task<string> SeedIntegrationAsync()
    {
        var id = "int-sc-" + Guid.NewGuid().ToString("N")[..10];
        await _db.ExecuteAsync(
            $"""
            INSERT INTO Integrations (Id, DisplayName, ModuleId, ScopeKey, ResourceContextId, State, CreatedAt, UpdatedAt)
            VALUES (N'{id}', N'Schedule Test Integration', N'Sample.Mod', N'instance', N'shared', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);
        return id;
    }

    private async Task<string> SeedScheduleAsync(bool enabled)
    {
        var id = "sch-" + Guid.NewGuid().ToString("N")[..12];
        await _db.ExecuteAsync(
            $"""
            INSERT INTO ScheduleDefinitions (Id, IntegrationId, ResourceContextId, Name, RecurrenceExpression, TimeZone, Enabled, TargetJobType, TargetJobConfigurationJson, ExecutionPolicyRef, Version)
            VALUES (N'{id}', NULL, N'shared', N'Nightly import', N'{Recurrence}', N'UTC', {(enabled ? 1 : 0)}, 0, N'{TargetJson}', N'policy-ref-sentinel', 1);
            """);
        return id;
    }

    private async Task<(string Admin, Principal Principal)> AdminAsync()
    {
        var id = NewUserId();
        await _db.AddUserAsync(id, NewUsername("admin"));
        return (id, new Principal(id));
    }

    private Task<List<Dictionary<string, object?>>> AuditRowsAsync() => _db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

    private static string? Detail(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        if (!details.RootElement.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static IReadOnlyList<string> Changed(Dictionary<string, object?> row)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.TryGetProperty("changed", out var changed)
            ? changed.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
    }

    private sealed class StubClaimOptions : ISchedulerClaimOptions
    {
        public int ClaimLeaseMinutes => 5;
    }
}
