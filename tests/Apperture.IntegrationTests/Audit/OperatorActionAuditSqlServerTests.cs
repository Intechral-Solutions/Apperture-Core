using System.Data.Common;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.OperationItems;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.Core.Domain.Workflow;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.3 operator action attribution against SQL Server (audit hardening design gate §10.5, §13, §22): each operator action
/// on a run commits its durable request, the request row's requester and its audit row together or not at all, and the request
/// row and the audit row name the same actor; a lost acknowledgement is verified and reconstructed, never repeated; an
/// unverifiable commit stays outcome_unknown; a concurrent duplicate commits nothing and records nothing. Fails, never skips,
/// without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class OperatorActionAuditSqlServerTests : IAsyncLifetime
{
    private const string WorkflowKey = "Test.Module.Workflow";
    private const string OperationKey = "Test.Module.Operation";
    private const string SecretLookingValue = "hunter2-do-not-record";

    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public OperatorActionAuditSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync() => _db = await AuditSqlScratch.CreateAsync("Apperture_Aud13Ops_");

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- Cancel ----

    [Fact]
    public async Task A_cancel_commits_its_intent_its_event_and_its_row_with_one_actor()
    {
        var (user, username) = await AddUserAsync("canceller");
        await SeedWorkflowRunAsync("run-c1", "failed");

        await using (var db = _db.NewDb())
            (await CancelService(db, user).RequestCancelAsync(new OperationRunId("run-c1"))).Should().BeTrue();

        (await _db.ScalarAsync<string>("SELECT RequestedBy FROM WorkflowCancelRequests WHERE RunId = N'run-c1';")).Should().Be(user);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE RunId = N'run-c1' AND EventType = N'workflow.cancel_requested';")).Should().Be(1);
        var row = (await RowsAsync("workflow_run", "run-c1")).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.WorkflowRunCancelRequested);
        row["Actor"].Should().Be(user, "the request row's RequestedBy and the audit row's Actor are the same value");
        row["ActorUserId"].Should().Be(user);
        row["ActorUsername"].Should().Be(username, "read in the transaction that recorded the row, never from the session claim");
        row["Basis"].Should().Be("request");
        row["OutcomeCode"].Should().Be("requested");
        row["RunId"].Should().Be("run-c1");
        row["EventId"].Should().Be(row["OperationEventId"], "the primary row carries the operation's event id");
    }

    [Fact]
    public async Task When_the_audit_insert_fails_a_cancel_leaves_no_intent_and_no_event()
    {
        var (user, _) = await AddUserAsync("canceller");
        await SeedWorkflowRunAsync("run-c2", "running");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => CancelService(db, user).RequestCancelAsync(new OperationRunId("run-c2")).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowCancelRequests;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE EventType = N'workflow.cancel_requested';")).Should().Be(0);
        (await RowsAsync("workflow_run", "run-c2")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancel_whose_acknowledgement_is_lost_is_verified_reported_as_done_and_not_repeated()
    {
        var (user, _) = await AddUserAsync("canceller");
        await SeedWorkflowRunAsync("run-c3", "running");
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using (var db = _db.NewDb(lost, attempts))
            (await CancelService(db, user).RequestCancelAsync(new OperationRunId("run-c3"))).Should().BeTrue();

        attempts.Started.Should().Be(1, "verification confirmed the commit, so nothing reran");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowCancelRequests WHERE RunId = N'run-c3';")).Should().Be(1);
        (await RowsAsync("workflow_run", "run-c3")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_cancel_whose_commit_cannot_be_verified_is_outcome_unknown_and_never_half_applied()
    {
        var (user, _) = await AddUserAsync("canceller");
        await SeedWorkflowRunAsync("run-c4", "running");
        var lost = new LostCommitAcknowledgement();

        await using (var db = _db.NewDb(lost))
        {
            var unit = NewUnit(db, new Principal(user), verification: new UnreachableVerification());
            var act = () => new WorkflowCancelService(db, new AllowCancel(), unit).RequestCancelAsync(new OperationRunId("run-c4")).AsTask();
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        }

        // Either both committed or neither did, never one without the other.
        var requests = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowCancelRequests WHERE RunId = N'run-c4';");
        var rows = (await RowsAsync("workflow_run", "run-c4")).Count;
        requests.Should().Be(rows);
    }

    [Fact]
    public async Task A_cancel_that_loses_a_race_for_the_one_intent_commits_nothing_and_records_nothing()
    {
        var (user, _) = await AddUserAsync("canceller");
        await SeedWorkflowRunAsync("run-c5", "running");
        var race = new BeforeFirstMatchingCommand(
            sql => sql.Contains("INSERT INTO [WorkflowCancelRequests]", StringComparison.Ordinal),
            async () => await _db.ExecuteAsync(
                "INSERT INTO WorkflowCancelRequests (RunId, WorkflowInstanceId, WorkflowDefinitionId, RequestedAt, RequestedBy) "
                + $"VALUES (N'run-c5', N'inst-run-c5', N'{WorkflowKey}', SYSDATETIMEOFFSET(), N'usr_winner');"));

        await using (var db = _db.NewDb(race))
            (await CancelService(db, user).RequestCancelAsync(new OperationRunId("run-c5"))).Should().BeTrue("the intent exists, which is what the caller asked for");

        race.Fired.Should().BeTrue();
        (await _db.ScalarAsync<string>("SELECT RequestedBy FROM WorkflowCancelRequests WHERE RunId = N'run-c5';")).Should().Be("usr_winner");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE RunId = N'run-c5' AND EventType = N'workflow.cancel_requested';"))
            .Should().Be(0, "this request's event rolled back with its intent");
        (await RowsAsync("workflow_run", "run-c5")).Should().BeEmpty("a request that committed nothing records nothing");
    }

    [Fact]
    public async Task A_repeated_cancel_records_nothing_the_second_time()
    {
        var (user, _) = await AddUserAsync("canceller");
        await SeedWorkflowRunAsync("run-c6", "running");

        for (var i = 0; i < 2; i++)
        {
            await using var db = _db.NewDb();
            (await CancelService(db, user).RequestCancelAsync(new OperationRunId("run-c6"))).Should().BeTrue();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowCancelRequests WHERE RunId = N'run-c6';")).Should().Be(1);
        (await RowsAsync("workflow_run", "run-c6")).Should().ContainSingle();
    }

    // ---- Park ----

    [Fact]
    public async Task A_park_request_commits_with_its_row_and_its_actor()
    {
        var (user, _) = await AddUserAsync("parker");
        await SeedWorkflowRunAsync("run-p1", "running");

        await using (var db = _db.NewDb())
            (await ControlService(db, user).RequestParkAsync(new OperationRunId("run-p1")))!.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);

        (await _db.ScalarAsync<string>("SELECT RequestedBy FROM WorkflowExternalControlRequests WHERE RunId = N'run-p1';")).Should().Be(user);
        var row = (await RowsAsync("workflow_run", "run-p1")).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.WorkflowRunParkRequested);
        row["Actor"].Should().Be(user);
    }

    [Fact]
    public async Task When_the_audit_insert_fails_a_park_request_leaves_no_request_and_no_event()
    {
        var (user, _) = await AddUserAsync("parker");
        await SeedWorkflowRunAsync("run-p2", "running");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => ControlService(db, user).RequestParkAsync(new OperationRunId("run-p2")).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowExternalControlRequests;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE EventType = N'workflow.park_requested';")).Should().Be(0);
    }

    [Fact]
    public async Task A_park_request_whose_acknowledgement_is_lost_reports_the_committed_request()
    {
        var (user, _) = await AddUserAsync("parker");
        await SeedWorkflowRunAsync("run-p3", "running");
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        WorkflowExternalControlResult? result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await ControlService(db, user).RequestParkAsync(new OperationRunId("run-p3"));

        attempts.Started.Should().Be(1);
        result!.Status.Should().Be(WorkflowExternalControlStatusNames.Queued);
        result.ControlKind.Should().Be(WorkflowExternalControlKindNames.Park);
        (await RowsAsync("workflow_run", "run-p3")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_park_request_that_loses_the_unique_index_race_commits_nothing_and_records_nothing()
    {
        var (user, _) = await AddUserAsync("parker");
        await SeedWorkflowRunAsync("run-p4", "running");
        var race = new BeforeFirstMatchingCommand(
            sql => sql.Contains("INSERT INTO [WorkflowExternalControlRequests]", StringComparison.Ordinal),
            async () =>
            {
                await using var other = _db.NewDb();
                other.WorkflowExternalControlRequests.Add(new WorkflowExternalControlRequestEntity
                {
                    RunId = "run-p4",
                    WorkflowInstanceId = "inst-run-p4",
                    WorkflowDefinitionId = WorkflowKey,
                    ControlKind = WorkflowExternalControlKindNames.Park,
                    Status = WorkflowExternalControlStatusNames.Queued,
                    RequestedAt = DateTimeOffset.UtcNow,
                    RequestedBy = "usr_winner",
                });
                await other.SaveChangesAsync();
            });

        WorkflowExternalControlResult? result;
        await using (var db = _db.NewDb(race))
            result = await ControlService(db, user).RequestParkAsync(new OperationRunId("run-p4"));

        race.Fired.Should().BeTrue();
        result!.Status.Should().Be(WorkflowExternalControlStatusNames.Queued, "the winner's request is the one reported");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowExternalControlRequests WHERE RunId = N'run-p4';")).Should().Be(1);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE RunId = N'run-p4' AND EventType = N'workflow.park_requested';")).Should().Be(0);
        (await RowsAsync("workflow_run", "run-p4")).Should().BeEmpty();
    }

    // ---- Start (workflow, operation) ----

    [Fact]
    public async Task A_workflow_start_commits_the_run_its_request_and_its_row_with_the_actor_the_unit_resolved()
    {
        var (user, username) = await AddUserAsync("starter");

        WorkflowStartDispatchResult? result;
        await using (var db = _db.NewDb())
        {
            // A caller cannot name the requester: whatever is supplied is replaced by the unit's own actor.
            result = await StartService(db, user).StartWorkflowAsync(
                WorkflowStart(requestedBy: "usr_forged", idempotencyKey: "start-1"),
                ExecutionLaunchChannel.Direct);
        }

        result!.IsReplay.Should().BeFalse();
        var runId = result.RunId.Value;
        (await _db.ScalarAsync<string>($"SELECT RequestedBy FROM WorkflowStartRequests WHERE RunId = N'{runId}';")).Should().Be(user);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM WorkflowInstances WHERE Id = N'{result.WorkflowInstanceId.Value}';")).Should().Be(1);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE RunId = N'{runId}' AND EventType = N'workflow.dispatched';")).Should().Be(1);

        var row = (await RowsAsync("workflow_run", runId)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.WorkflowStartRequested);
        row["Actor"].Should().Be(user);
        row["ActorUsername"].Should().Be(username);
        row["Basis"].Should().Be("request");
        row["RunId"].Should().Be(runId);

        // Parameter names, never values.
        var details = (string)row["Details"]!;
        details.Should().Contain("\"parameterNames\":[\"message\"]").And.Contain("\"parameterCount\":1").And.Contain("\"launch\":\"direct\"");
        details.Should().NotContain(SecretLookingValue);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Details LIKE N'%{SecretLookingValue}%';")).Should().Be(0);
    }

    [Fact]
    public async Task When_the_audit_insert_fails_a_workflow_start_creates_no_instance_run_request_or_event()
    {
        var (user, _) = await AddUserAsync("starter");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => StartService(db, user).StartWorkflowAsync(WorkflowStart(), ExecutionLaunchChannel.Direct).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        foreach (var table in new[] { "WorkflowInstances", "OperationRuns", "WorkflowStartRequests", "WorkflowLifecycleEvents" })
            (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM {table};")).Should().Be(0, $"{table} rolled back with the audit row");
    }

    [Fact]
    public async Task A_workflow_start_whose_acknowledgement_is_lost_returns_the_committed_run_and_is_not_repeated()
    {
        var (user, _) = await AddUserAsync("starter");
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        WorkflowStartDispatchResult? result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await StartService(db, user).StartWorkflowAsync(WorkflowStart(), ExecutionLaunchChannel.Direct);

        attempts.Started.Should().Be(1);
        var runId = result!.RunId.Value;
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns;")).Should().Be(1);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM WorkflowStartRequests WHERE RunId = N'{runId}' AND RequestedBy = N'{user}';")).Should().Be(1);
        (await RowsAsync("workflow_run", runId)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_workflow_start_whose_commit_cannot_be_verified_is_outcome_unknown()
    {
        var (user, _) = await AddUserAsync("starter");
        var lost = new LostCommitAcknowledgement();

        await using (var db = _db.NewDb(lost))
        {
            var unit = NewUnit(db, new Principal(user), verification: new UnreachableVerification());
            var act = () => new AuditedExecutionStartService(
                unit, NewEvents(user), Workflows(db), Operations(db), new Principal(user), db)
                .StartWorkflowAsync(WorkflowStart(), ExecutionLaunchChannel.Direct).AsTask();
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        }

        var runs = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns;");
        var rows = await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.WorkflowStartRequested}';");
        rows.Should().Be(runs, "the run and its row committed together or not at all");
    }

    [Fact]
    public async Task A_repeated_start_with_the_same_idempotency_key_returns_the_earlier_run_and_records_nothing_new()
    {
        var (user, _) = await AddUserAsync("starter");
        var (other, _) = await AddUserAsync("other");

        WorkflowStartDispatchResult? first, second;
        await using (var db = _db.NewDb())
            first = await StartService(db, user).StartWorkflowAsync(WorkflowStart(idempotencyKey: "same-key"), ExecutionLaunchChannel.Direct);
        await using (var db = _db.NewDb())
            second = await StartService(db, other).StartWorkflowAsync(WorkflowStart(idempotencyKey: "same-key"), ExecutionLaunchChannel.Direct);

        second!.RunId.Should().Be(first!.RunId);
        second.IsReplay.Should().BeTrue();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns;")).Should().Be(1);
        (await _db.ScalarAsync<string>("SELECT RequestedBy FROM WorkflowStartRequests;")).Should().Be(user, "the replay did not take over the earlier request");
        (await RowsAsync("workflow_run", first.RunId.Value)).Should().ContainSingle().Which["Actor"].Should().Be(user);
    }

    [Fact]
    public async Task A_start_that_loses_the_idempotency_race_inside_its_unit_creates_nothing_and_records_nothing()
    {
        var (user, _) = await AddUserAsync("starter");
        var race = new BeforeFirstMatchingCommand(
            sql => sql.Contains("INSERT INTO [WorkflowStartRequests]", StringComparison.Ordinal),
            async () => await _db.ExecuteAsync(
                "INSERT INTO WorkflowInstances (Id, DefinitionId, Status, CreatedAt, UpdatedAt, Version) "
                + $"VALUES (N'winner-inst', N'{WorkflowKey}', N'requested', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0);"
                + " INSERT INTO WorkflowStartRequests (RunId, WorkflowInstanceId, WorkflowDefinitionId, IdempotencyKey, CreatedAt, Status, RequestedBy) "
                + $"VALUES (N'winner-run', N'winner-inst', N'{WorkflowKey}', N'race-key', SYSDATETIMEOFFSET(), N'requested', N'usr_winner');"));

        WorkflowStartDispatchResult? result;
        await using (var db = _db.NewDb(race))
            result = await StartService(db, user).StartWorkflowAsync(WorkflowStart(idempotencyKey: "race-key"), ExecutionLaunchChannel.Direct);

        race.Fired.Should().BeTrue();
        result!.RunId.Value.Should().Be("winner-run", "the winner's run is the one for this idempotency key");
        result.IsReplay.Should().BeTrue();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowStartRequests;")).Should().Be(1);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns;")).Should().Be(0, "this attempt's run rolled back to the savepoint");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowInstances;")).Should().Be(1, "only the winner's instance remains");
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{AuditActionNames.WorkflowStartRequested}';")).Should().Be(0);
    }

    [Fact]
    public async Task A_start_of_a_definition_that_is_not_executable_creates_nothing_and_is_recorded_as_a_refusal_by_the_same_actor()
    {
        var (user, _) = await AddUserAsync("starter");

        WorkflowStartDispatchResult? result;
        await using (var db = _db.NewDb())
            result = await StartService(db, user).StartWorkflowAsync(
                WorkflowStart() with { WorkflowDefinitionId = new WorkflowDefinitionId("Missing.Module.Workflow") },
                ExecutionLaunchChannel.Procedure);

        result.Should().BeNull();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns;")).Should().Be(0);
        var row = (await _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{AuditActionNames.WorkflowStartBlocked}';")).Should().ContainSingle().Which;
        row["Actor"].Should().Be(user, "the refusal is the requester's, not the platform's");
        row["ActorKind"].Should().Be("user");
        row["Basis"].Should().Be("attempt");
        row["OutcomeCode"].Should().Be("blocked");
        row["TargetType"].Should().BeNull("no run exists to target");
        ((string)row["Details"]!).Should().Contain("\"launch\":\"procedure\"").And.Contain("Missing.Module.Workflow");
    }

    [Fact]
    public async Task An_operation_start_commits_the_run_its_request_and_its_row_together()
    {
        var (user, _) = await AddUserAsync("starter");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => StartService(db, user).StartOperationAsync(OperationStart(), ExecutionLaunchChannel.Direct).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        foreach (var table in new[] { "OperationInstances", "OperationRuns", "OperationStartRequests", "OperationLifecycleEvents" })
            (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM {table};")).Should().Be(0, $"{table} rolled back with the audit row");

        OperationStartDispatchResult? result;
        await using (var db = _db.NewDb())
            result = await StartService(db, user).StartOperationAsync(OperationStart(), ExecutionLaunchChannel.Procedure);

        var runId = result!.RunId.Value;
        (await _db.ScalarAsync<string>($"SELECT RequestedBy FROM OperationStartRequests WHERE RunId = N'{runId}';")).Should().Be(user);
        var row = (await RowsAsync("operation_run", runId)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.OperationStartRequested);
        row["Actor"].Should().Be(user);
        ((string)row["Details"]!).Should().Contain("\"launch\":\"procedure\"").And.NotContain(SecretLookingValue);
    }

    // ---- Retry ----

    [Fact]
    public async Task A_workflow_retry_commits_the_new_run_its_requester_the_original_runs_event_and_its_row_together()
    {
        var (user, _) = await AddUserAsync("retrier");
        await SeedWorkflowRunAsync("run-r1", "failed", withStartRequest: true, resourceContextId: "ctx-a");

        WorkflowRetryResult? result;
        await using (var db = _db.NewDb())
            result = await RetryService(db, user).RequestRetryAsync(new OperationRunId("run-r1"));

        var retryRunId = result!.RunId!.Value;
        (await _db.ScalarAsync<string>($"SELECT RequestedBy FROM WorkflowStartRequests WHERE RunId = N'{retryRunId}';")).Should().Be(user);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE RunId = N'run-r1' AND EventType = N'workflow.retry_requested';")).Should().Be(1);
        var row = (await RowsAsync("workflow_run", "run-r1")).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.WorkflowRetryRequested);
        row["Actor"].Should().Be(user);
        row["ResourceContextId"].Should().Be("ctx-a");
        ((string)row["Details"]!).Should().Contain($"\"retryRunId\":\"{retryRunId}\"");
    }

    [Fact]
    public async Task When_the_audit_insert_fails_a_workflow_retry_creates_no_run_and_adds_no_event()
    {
        var (user, _) = await AddUserAsync("retrier");
        await SeedWorkflowRunAsync("run-r2", "failed", withStartRequest: true);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => RetryService(db, user).RequestRetryAsync(new OperationRunId("run-r2")).AsTask();
            await act.Should().ThrowAsync<Exception>();
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns;")).Should().Be(1, "only the original run");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowStartRequests;")).Should().Be(1, "only the original request");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WorkflowLifecycleEvents WHERE EventType = N'workflow.retry_requested';")).Should().Be(0);
    }

    [Fact]
    public async Task A_workflow_retry_whose_acknowledgement_is_lost_reports_the_committed_retry()
    {
        var (user, _) = await AddUserAsync("retrier");
        await SeedWorkflowRunAsync("run-r3", "failed", withStartRequest: true);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        WorkflowRetryResult? result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await RetryService(db, user).RequestRetryAsync(new OperationRunId("run-r3"), WorkflowRetryChannel.BlockedWork);

        attempts.Started.Should().Be(1);
        result!.RunId.Should().NotBeNull();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns WHERE RetryOfRunId = N'run-r3';")).Should().Be(1);
        var row = (await RowsAsync("workflow_run", "run-r3")).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.BlockedWorkRetryRequested, "the recovery channel records the same fact under its own action");
    }

    [Fact]
    public async Task A_workflow_retry_of_a_definition_that_is_not_executable_creates_nothing_and_is_recorded_as_a_refusal_by_the_same_actor()
    {
        var (user, _) = await AddUserAsync("retrier");
        await SeedWorkflowRunAsync("run-r4", "failed", withStartRequest: true, resourceContextId: "ctx-b");

        WorkflowRetryResult? result;
        await using (var db = _db.NewDb())
        {
            var service = new WorkflowRetryService(
                db,
                new AllowRetry(),
                new WorkflowStartDispatchService(new FixedWorkflowResolver(null), new SqlWorkflowPersistence(db), db),
                NewUnit(db, new Principal(user)),
                NewEvents(user),
                new Principal(user));
            result = await service.RequestRetryAsync(new OperationRunId("run-r4"));
        }

        result!.IsBlocked.Should().BeTrue();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns;")).Should().Be(1);
        var row = (await RowsAsync("workflow_run", "run-r4")).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.WorkflowRetryBlocked);
        row["Actor"].Should().Be(user, "the earlier code recorded the literal 'system' here");
        row["ResourceContextId"].Should().Be("ctx-b");
        row["Basis"].Should().Be("attempt");
    }

    // ---- Single-item retry ----

    [Fact]
    public async Task An_item_retry_commits_the_attempt_the_child_run_the_item_stamp_and_its_row_together_with_one_actor()
    {
        var (user, _) = await AddUserAsync("itemretrier");
        var (runId, item) = await SeedFailedItemAsync();

        OperationItemRetryAttemptDto? result;
        await using (var db = _db.NewDb())
            result = await ItemRetryService(db, user, new TestHandler()).RequestRetryAsync(
                new OperationRunId(runId), item.OperationItemId, new RequestOperationItemRetryDto("key-1", "operator reason"));

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
        result.RequestedByActorId.Should().Be(user);
        var childRunId = result.ChildRunId!;
        (await _db.ScalarAsync<string>($"SELECT RequestedBy FROM WorkflowStartRequests WHERE RunId = N'{childRunId}';")).Should().Be(user);
        (await _db.ScalarAsync<int>($"SELECT RetryCount FROM OperationItems WHERE Id = N'{item.OperationItemId}';")).Should().Be(1);
        var row = (await RowsAsync("operation_item", item.OperationItemId)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.OperationItemRetryRequested);
        row["Actor"].Should().Be(user, "the attempt's requester and the audit row's actor are the same value");
        row["RunId"].Should().Be(runId);
        var details = (string)row["Details"]!;
        details.Should().Contain($"\"attemptId\":\"{result.OperationItemRetryAttemptId}\"").And.Contain($"\"childRunId\":\"{childRunId}\"");
        details.Should().Contain("operator reason").And.NotContain("key-1", "the client's idempotency key is not audit content");
    }

    [Fact]
    public async Task When_the_audit_insert_fails_an_item_retry_leaves_no_attempt_no_child_run_and_no_stamp()
    {
        var (user, _) = await AddUserAsync("itemretrier");
        var (runId, item) = await SeedFailedItemAsync();
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => ItemRetryService(db, user, new TestHandler()).RequestRetryAsync(
                new OperationRunId(runId), item.OperationItemId, new RequestOperationItemRetryDto("key-2"));
            await act.Should().ThrowAsync<Exception>();
        }

        // Before AUD-1.3 the attempt, the dispatch and the dispatched mark were separate commits, so a failure between them
        // left an attempt that said "requested" with nothing started.
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemRetryAttempts;")).Should().Be(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns WHERE RetryOfRunId IS NOT NULL;")).Should().Be(0);
        (await _db.ScalarAsync<int>($"SELECT RetryCount FROM OperationItems WHERE Id = N'{item.OperationItemId}';")).Should().Be(0);
    }

    [Fact]
    public async Task An_item_retry_whose_acknowledgement_is_lost_reports_the_committed_attempt()
    {
        var (user, _) = await AddUserAsync("itemretrier");
        var (runId, item) = await SeedFailedItemAsync();
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        OperationItemRetryAttemptDto? result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await ItemRetryService(db, user, new TestHandler()).RequestRetryAsync(
                new OperationRunId(runId), item.OperationItemId, new RequestOperationItemRetryDto("key-3"));

        attempts.Started.Should().Be(1);
        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Dispatched);
        result.ChildRunId.Should().NotBeNullOrEmpty();
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemRetryAttempts;")).Should().Be(1);
        (await RowsAsync("operation_item", item.OperationItemId)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_blocked_item_retry_records_its_committed_attempt_as_a_blocked_outcome_by_the_requester()
    {
        var (user, _) = await AddUserAsync("itemretrier");
        var (runId, item) = await SeedFailedItemAsync();

        OperationItemRetryAttemptDto? result;
        await using (var db = _db.NewDb())
            result = await ItemRetryService(db, user, new TestHandler { CanRetry = false }).RequestRetryAsync(
                new OperationRunId(runId), item.OperationItemId, new RequestOperationItemRetryDto("key-4"));

        result!.Status.Should().Be(OperationItemRetryAttemptStatuses.Blocked);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns WHERE RetryOfRunId IS NOT NULL;")).Should().Be(0);
        var row = (await RowsAsync("operation_item", item.OperationItemId)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.OperationItemRetryBlocked);
        row["OutcomeCode"].Should().Be("blocked");
        row["Basis"].Should().Be("change");
        row["Actor"].Should().Be(user);
    }

    [Fact]
    public async Task A_repeated_item_retry_with_the_same_key_creates_no_second_attempt_and_records_nothing_new()
    {
        var (user, _) = await AddUserAsync("itemretrier");
        var (runId, item) = await SeedFailedItemAsync();

        for (var i = 0; i < 2; i++)
        {
            await using var db = _db.NewDb();
            await ItemRetryService(db, user, new TestHandler()).RequestRetryAsync(
                new OperationRunId(runId), item.OperationItemId, new RequestOperationItemRetryDto("same-key"));
        }

        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemRetryAttempts;")).Should().Be(1);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns WHERE RetryOfRunId IS NOT NULL;")).Should().Be(1);
        (await RowsAsync("operation_item", item.OperationItemId)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_item_retry_that_loses_the_race_for_its_key_records_nothing_and_reports_the_winners_attempt(bool blocked)
    {
        var (user, _) = await AddUserAsync("itemretrier");
        var (runId, item) = await SeedFailedItemAsync();
        // The competing request commits its attempt with the same key (and so the same next attempt index) just before this
        // one inserts. In the blocked case the winner's attempt is blocked too, which is what the earlier guard missed.
        var race = new BeforeFirstMatchingCommand(
            sql => sql.Contains("INSERT INTO [OperationItemRetryAttempts]", StringComparison.Ordinal),
            async () => await _db.ExecuteAsync(
                "INSERT INTO OperationItemRetryAttempts (Id, OperationItemId, RunId, OperationGroupId, AttemptIndex, IdempotencyKey, RequestedByActorId, Status, StartedAtUtc, CreatedAtUtc, UpdatedAtUtc) "
                + $"VALUES (N'att-winner', N'{item.OperationItemId}', N'{runId}', N'{item.OperationGroupId}', 1, N'race-key', N'usr_winner', N'{(blocked ? "Blocked" : "Dispatched")}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());"));

        OperationItemRetryAttemptDto? result;
        await using (var db = _db.NewDb(race))
            result = await ItemRetryService(db, user, new TestHandler { CanRetry = !blocked }).RequestRetryAsync(
                new OperationRunId(runId), item.OperationItemId, new RequestOperationItemRetryDto("race-key"));

        race.Fired.Should().BeTrue();
        result!.OperationItemRetryAttemptId.Should().Be("att-winner", "the request is answered by the attempt that won");
        result.RequestedByActorId.Should().Be("usr_winner");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemRetryAttempts;")).Should().Be(1);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationRuns WHERE RetryOfRunId IS NOT NULL;")).Should().Be(0);
        (await RowsAsync("operation_item", item.OperationItemId)).Should().BeEmpty("the attempt and its row belong to the request that made it");
    }

    [Fact]
    public async Task A_confirmed_commit_whose_result_cannot_be_rebuilt_is_reported_as_applied_never_as_a_refusal()
    {
        var (user, _) = await AddUserAsync("canceller");
        await SeedWorkflowRunAsync("run-rc1", "running");
        var lost = new LostCommitAcknowledgement();

        await using (var db = _db.NewDb(lost))
        {
            var act = () => NewUnit(db, new Principal(user)).ExecuteAsync<bool>(
                new AuditedOperation(AuditActionNames.WorkflowRunCancelRequested, AuditActor.CurrentPrincipal),
                async (transaction, ct) =>
                {
                    await transaction.StageAsync(AuditEvents.RunCancelRequested(true, "run-rc1", "inst-run-rc1", WorkflowKey, "running", null), ct);
                    return true;
                },
                (_, _) => throw new InvalidOperationException("The committed request could not be read back."));

            // An InvalidOperationException here would read as a 409 or 400 refusal at the endpoints that map it so.
            var thrown = await act.Should().ThrowAsync<AuditCommittedResultUnavailableException>();
            thrown.Which.Should().NotBeAssignableTo<InvalidOperationException>();
            thrown.Which.OperationEventId.Should().NotBeNullOrEmpty();
        }

        (await RowsAsync("workflow_run", "run-rc1")).Should().ContainSingle("the change committed; only its result could not be rebuilt");
    }

    // ---- Bulk retry (orchestration over item retries) ----

    [Fact]
    public async Task A_bulk_retry_commits_each_item_with_its_own_row_naming_the_batch_and_writes_no_batch_row()
    {
        var (user, _) = await AddUserAsync("bulkretrier");
        var (runId, items) = await SeedFailedItemsAsync(2);

        BulkRetryOperationItemsResult? result;
        await using (var db = _db.NewDb())
            result = await BulkService(db, user).BulkRetryAsync(
                new OperationRunId(runId), items[0].OperationGroupId, new BulkRetryOperationItemsRequest([.. items.Select(i => i.OperationItemId)], RequestId: "batch-sql"));

        result!.DispatchedCount.Should().Be(2);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM OperationItemRetryAttempts WHERE RequestedByActorId = N'{user}';")).Should().Be(2);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action LIKE N'execution.operation_item.bulk_retry.%';"))
            .Should().Be(0, "the batch changes nothing itself, so it records no row of its own");
        foreach (var item in items)
        {
            var row = (await RowsAsync("operation_item", item.OperationItemId)).Should().ContainSingle().Which;
            row["Action"].Should().Be(AuditActionNames.OperationItemRetryRequested);
            row["Actor"].Should().Be(user);
            JsonDocument.Parse((string)row["Details"]!).RootElement.GetProperty("bulkRequestId").GetString().Should().Be("batch-sql");
        }
    }

    [Fact]
    public async Task A_bulk_retry_whose_second_item_cannot_commit_keeps_the_first_and_reports_the_second()
    {
        var (user, _) = await AddUserAsync("bulkretrier");
        var (runId, items) = await SeedFailedItemsAsync(2);
        // The second item's audit insert fails: that item's unit rolls back alone; the first has committed and stays.
        var auditInserts = 0;
        var failSecondAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal) && ++auditInserts == 2);

        BulkRetryOperationItemsResult? result;
        await using (var db = _db.NewDb(failSecondAudit))
            result = await BulkService(db, user).BulkRetryAsync(
                new OperationRunId(runId), items[0].OperationGroupId, new BulkRetryOperationItemsRequest([.. items.Select(i => i.OperationItemId)], RequestId: "batch-partial"));

        result!.Items.Select(i => i.Category).Should().Equal(
            BulkRetryOperationItemResultCategories.Dispatched, BulkRetryOperationItemResultCategories.DispatchFailed);
        (await RowsAsync("operation_item", items[0].OperationItemId)).Should().ContainSingle();
        (await RowsAsync("operation_item", items[1].OperationItemId)).Should().BeEmpty();
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM OperationItemRetryAttempts WHERE OperationItemId = N'{items[1].OperationItemId}';"))
            .Should().Be(0, "the failed item's attempt rolled back with its row");
    }

    // ---- Attention acknowledge and reopen ----

    [Fact]
    public async Task An_acknowledge_commits_its_acknowledgements_and_one_row_on_the_run_with_one_actor()
    {
        var (user, username) = await AddUserAsync("acknowledger");
        var (runId, items) = await SeedFailedItemsAsync(2);

        BulkAcknowledgeOperationItemsResult? result;
        await using (var db = _db.NewDb())
            result = await AttentionService(db, user).AcknowledgeAsync(
                new OperationRunId(runId), items[0].OperationGroupId,
                new BulkAcknowledgeOperationItemsRequest([.. items.Select(i => i.OperationItemId)], "will-not-retry", "Handled outside the platform."));

        result!.AcknowledgedCount.Should().Be(2);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM OperationItemAttentionAcknowledgements WHERE AcknowledgedByUserId = N'{user}';")).Should().Be(2);
        var row = (await RowsAsync("operation_run", runId)).Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.OperationItemAttentionAcknowledged);
        row["Actor"].Should().Be(user, "the acknowledgement rows and the audit row name the same actor");
        row["ActorUsername"].Should().Be(username);
        row["Basis"].Should().Be("change");
        row["OutcomeCode"].Should().Be("succeeded");
        var details = JsonDocument.Parse((string)row["Details"]!).RootElement;
        details.GetProperty("operationItemIds").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(items.Select(i => i.OperationItemId));
        details.GetProperty("disposition").GetString().Should().Be(OperationItemAttentionDispositions.WillNotRetry);
    }

    [Fact]
    public async Task When_the_audit_insert_fails_an_acknowledge_leaves_no_acknowledgement()
    {
        var (user, _) = await AddUserAsync("acknowledger");
        var (runId, items) = await SeedFailedItemsAsync(2);
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = _db.NewDb(failAudit))
        {
            var act = () => AttentionService(db, user).AcknowledgeAsync(
                new OperationRunId(runId), items[0].OperationGroupId,
                new BulkAcknowledgeOperationItemsRequest([.. items.Select(i => i.OperationItemId)], "will-not-retry"));
            await act.Should().ThrowAsync<Exception>();
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemAttentionAcknowledgements;"))
            .Should().Be(0, "before AUD-1.3 each acknowledgement committed on its own and the row was best effort afterwards");
    }

    [Fact]
    public async Task An_acknowledge_whose_acknowledgement_is_lost_is_verified_reconstructed_and_not_repeated()
    {
        var (user, _) = await AddUserAsync("acknowledger");
        var (runId, items) = await SeedFailedItemsAsync(1);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        BulkAcknowledgeOperationItemsResult? result;
        await using (var db = _db.NewDb(lost, attempts))
            result = await AttentionService(db, user).AcknowledgeAsync(
                new OperationRunId(runId), items[0].OperationGroupId,
                new BulkAcknowledgeOperationItemsRequest([items[0].OperationItemId], "will-not-retry"));

        attempts.Started.Should().Be(1, "verification confirmed the commit, so nothing reran");
        result!.Items.Should().ContainSingle().Which.Category.Should().Be(
            BulkAcknowledgeOperationItemResultCategories.Acknowledged, "a confirmed commit is never reported as already acknowledged");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemAttentionAcknowledgements;")).Should().Be(1);
        (await RowsAsync("operation_run", runId)).Should().ContainSingle();
    }

    [Fact]
    public async Task An_acknowledge_whose_commit_cannot_be_verified_is_outcome_unknown_and_never_half_applied()
    {
        var (user, _) = await AddUserAsync("acknowledger");
        var (runId, items) = await SeedFailedItemsAsync(1);
        var lost = new LostCommitAcknowledgement();

        await using (var db = _db.NewDb(lost))
        {
            var service = new OperationItemAttentionService(
                db, new SqlOperationItemStore(db), new SqlOperationItemAttentionAcknowledgementStore(db), new AllowRetry(), new Principal(user),
                NewUnit(db, new Principal(user), verification: new UnreachableVerification()));
            var act = () => service.AcknowledgeAsync(
                new OperationRunId(runId), items[0].OperationGroupId,
                new BulkAcknowledgeOperationItemsRequest([items[0].OperationItemId], "will-not-retry"));
            await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        }

        var acknowledgements = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemAttentionAcknowledgements;");
        var rows = (await RowsAsync("operation_run", runId)).Count;
        acknowledgements.Should().Be(rows, "either both committed or neither did");
    }

    [Fact]
    public async Task An_acknowledge_that_loses_the_race_for_an_items_active_acknowledgement_records_nothing()
    {
        var (user, _) = await AddUserAsync("acknowledger");
        var (runId, items) = await SeedFailedItemsAsync(1);
        var item = items[0];
        var race = new BeforeFirstMatchingCommand(
            sql => sql.Contains("INSERT INTO [OperationItemAttentionAcknowledgements]", StringComparison.Ordinal),
            async () => await _db.ExecuteAsync(
                "INSERT INTO OperationItemAttentionAcknowledgements (Id, OperationItemId, RunId, OperationGroupId, Disposition, AcknowledgedAtUtc, AcknowledgedByUserId, CreatedAtUtc) "
                + $"VALUES (N'ack-winner', N'{item.OperationItemId}', N'{runId}', N'{item.OperationGroupId}', N'NoActionRequired', SYSDATETIMEOFFSET(), N'usr_winner', SYSDATETIMEOFFSET());"));

        BulkAcknowledgeOperationItemsResult? result;
        await using (var db = _db.NewDb(race))
            result = await AttentionService(db, user).AcknowledgeAsync(
                new OperationRunId(runId), item.OperationGroupId,
                new BulkAcknowledgeOperationItemsRequest([item.OperationItemId], "will-not-retry"));

        race.Fired.Should().BeTrue();
        result!.AcknowledgedCount.Should().Be(0);
        result.Items.Should().ContainSingle().Which.Category.Should().Be(BulkAcknowledgeOperationItemResultCategories.AlreadyAcknowledged);
        result.Items[0].Acknowledgement!.AcknowledgedByUserId.Should().Be("usr_winner", "the winner's acknowledgement is never attributed to the loser");
        (await _db.ScalarAsync<int>("SELECT COUNT(*) FROM OperationItemAttentionAcknowledgements;")).Should().Be(1);
        (await RowsAsync("operation_run", runId)).Should().BeEmpty("a request that committed nothing records nothing");
    }

    [Fact]
    public async Task A_reopen_commits_with_its_row_and_one_that_loses_a_race_neither_overwrites_the_winner_nor_records_anything()
    {
        var (user, _) = await AddUserAsync("reopener");
        var (runId, items) = await SeedFailedItemsAsync(2);
        await _db.ExecuteAsync(string.Join(
            " ",
            items.Select((item, i) =>
                "INSERT INTO OperationItemAttentionAcknowledgements (Id, OperationItemId, RunId, OperationGroupId, Disposition, AcknowledgedAtUtc, AcknowledgedByUserId, CreatedAtUtc) "
                + $"VALUES (N'ack-{i}', N'{item.OperationItemId}', N'{runId}', N'{item.OperationGroupId}', N'WillNotRetry', SYSDATETIMEOFFSET(), N'usr_earlier', SYSDATETIMEOFFSET());")));

        // The first item reopens normally. For the second, a competing request reopens it just before this one's update.
        var race = new BeforeFirstMatchingCommand(
            sql => sql.TrimStart().StartsWith("UPDATE", StringComparison.Ordinal)
                && sql.Contains("OperationItemAttentionAcknowledgements", StringComparison.Ordinal),
            async () => await _db.ExecuteAsync(
                $"UPDATE OperationItemAttentionAcknowledgements SET ReopenedAtUtc = SYSDATETIMEOFFSET(), ReopenedByUserId = N'usr_winner' WHERE Id = N'ack-1';"));

        BulkReopenOperationItemsResult? first;
        await using (var db = _db.NewDb())
            first = await AttentionService(db, user).ReopenAttentionAsync(
                new OperationRunId(runId), items[0].OperationGroupId, new BulkReopenOperationItemsRequest([items[0].OperationItemId]));

        BulkReopenOperationItemsResult? second;
        await using (var db = _db.NewDb(race))
            second = await AttentionService(db, user).ReopenAttentionAsync(
                new OperationRunId(runId), items[1].OperationGroupId, new BulkReopenOperationItemsRequest([items[1].OperationItemId]));

        first!.ReopenedCount.Should().Be(1);
        (await _db.ScalarAsync<string>("SELECT ReopenedByUserId FROM OperationItemAttentionAcknowledgements WHERE Id = N'ack-0';")).Should().Be(user);
        var row = (await RowsAsync("operation_run", runId)).Should().ContainSingle("only the reopen that changed something is recorded").Which;
        row["Action"].Should().Be(AuditActionNames.OperationItemAttentionReopened);
        row["Actor"].Should().Be(user);

        race.Fired.Should().BeTrue();
        second!.ReopenedCount.Should().Be(0);
        second.Items.Should().ContainSingle().Which.Category.Should().Be(BulkAcknowledgeOperationItemResultCategories.NotAcknowledged);
        (await _db.ScalarAsync<string>("SELECT ReopenedByUserId FROM OperationItemAttentionAcknowledgements WHERE Id = N'ack-1';"))
            .Should().Be("usr_winner", "the conditional update never overwrites who reopened it");
    }

    // ---- wiring ----

    private async Task<(string Id, string Username)> AddUserAsync(string stem)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await _db.AddUserAsync(id, username);
        return (id, username);
    }

    private Task<List<Dictionary<string, object?>>> RowsAsync(string targetType, string targetId) =>
        _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'{targetType}' AND TargetId = N'{targetId}' ORDER BY Id;");

    private async Task SeedWorkflowRunAsync(string runId, string status, bool withStartRequest = false, string? resourceContextId = null)
    {
        await using var db = _db.NewDb();
        var instanceId = "inst-" + runId;
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = instanceId,
            DefinitionId = WorkflowKey,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Version = 1,
            CurrentStepName = "DoWork",
            ResourceContextId = resourceContextId,
        });
        db.OperationRuns.Add(new OperationRunEntity
        {
            Id = runId,
            WorkflowInstanceId = instanceId,
            DefinitionId = WorkflowKey,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = status is "failed" or "cancelled" ? DateTimeOffset.UtcNow.AddMinutes(-1) : null,
            CorrelationKey = "corr-" + runId,
            ResourceContextId = resourceContextId,
        });
        if (withStartRequest)
        {
            db.WorkflowStartRequests.Add(new WorkflowStartRequestEntity
            {
                RunId = runId,
                WorkflowInstanceId = instanceId,
                WorkflowDefinitionId = WorkflowKey,
                ParametersJson = "{\"message\":\"original\"}",
                CorrelationId = "corr-" + runId,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                Status = status,
                ResourceContextId = resourceContextId,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<(string RunId, OperationItem Item)> SeedFailedItemAsync()
    {
        var runId = "run-item-" + Guid.NewGuid().ToString("N")[..8];
        await SeedWorkflowRunAsync(runId, "failed", withStartRequest: true);
        await using var db = _db.NewDb();
        var items = new SqlOperationItemStore(db);
        var group = await items.BeginGroupAsync(new BeginOperationGroupRequest(runId, TestHandler.Module, null, "Test.Module.procedures.import", TestHandler.Kind));
        var item = await items.AppendItemAsync(new AppendOperationItemRequest(
            group.OperationGroupId, runId, 1, OperationItemStatuses.Failed,
            SubjectKey: "C-1", DisplayLabel: "C-1", ErrorCode: "invalid_email", ErrorMessage: "bad", IsRetryable: true,
            MetadataJson: "{\"rowNumber\":1}"));
        await items.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, 1, 0, 1, 0));
        return (runId, item);
    }

    private async Task<(string RunId, IReadOnlyList<OperationItem> Items)> SeedFailedItemsAsync(int count)
    {
        var runId = "run-items-" + Guid.NewGuid().ToString("N")[..8];
        await SeedWorkflowRunAsync(runId, "failed", withStartRequest: true);
        await using var db = _db.NewDb();
        var store = new SqlOperationItemStore(db);
        var group = await store.BeginGroupAsync(new BeginOperationGroupRequest(runId, TestHandler.Module, null, "Test.Module.procedures.import", TestHandler.Kind));
        var items = new List<OperationItem>();
        for (var i = 1; i <= count; i++)
        {
            items.Add(await store.AppendItemAsync(new AppendOperationItemRequest(
                group.OperationGroupId, runId, i, OperationItemStatuses.Failed,
                SubjectKey: $"C-{i}", DisplayLabel: $"C-{i}", ErrorCode: "invalid_email", ErrorMessage: "bad", IsRetryable: true,
                MetadataJson: $"{{\"rowNumber\":{i}}}")));
        }

        await store.CompleteGroupAsync(new CompleteOperationGroupRequest(
            group.OperationGroupId, OperationGroupStatuses.CompletedWithErrors, count, 0, count, 0));
        return (runId, items);
    }

    private OperationItemAttentionService AttentionService(AppertureDbContext db, string user) =>
        new(
            db,
            new SqlOperationItemStore(db),
            new SqlOperationItemAttentionAcknowledgementStore(db),
            new AllowRetry(),
            new Principal(user),
            NewUnit(db, new Principal(user)));

    private BulkOperationItemRetryService BulkService(AppertureDbContext db, string user) =>
        new(
            db,
            new SqlOperationItemStore(db),
            ItemRetryService(db, user, new TestHandler()),
            new AllowRetry(),
            new Principal(user),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BulkOperationItemRetryService>.Instance);

    private static StartWorkflowRequest WorkflowStart(string? requestedBy = null, string? idempotencyKey = null) =>
        new(
            new WorkflowDefinitionId(WorkflowKey),
            new Dictionary<string, object?> { ["message"] = SecretLookingValue },
            idempotencyKey,
            "corr-start",
            ExecutionOrigin: RunExecutionOriginNames.Manual,
            RequestedBy: requestedBy);

    private static StartOperationRequest OperationStart() =>
        new(
            OperationKey,
            new Dictionary<string, object?> { ["message"] = SecretLookingValue },
            null,
            "corr-op",
            ExecutionOrigin: RunExecutionOriginNames.Manual);

    private SqlAuditEventWriter NewEvents(string user) => new(_db.Options(), TimeProvider.System, null, new Principal(user));

    private static WorkflowStartDispatchService Workflows(AppertureDbContext db) =>
        new(new FixedWorkflowResolver(new WorkflowDefinition(new WorkflowDefinitionId(WorkflowKey), "Workflow", "Test.Module", ["Step1"])), new SqlWorkflowPersistence(db), db);

    private static OperationStartDispatchService Operations(AppertureDbContext db) =>
        new(new FixedOperationRegistry(new Apperture.Core.Domain.Operations.OperationDefinition(
            new OperationId(OperationKey), "Operation", "Test.Module", OperationCategory.Command, null, Array.Empty<Apperture.Shared.Kernel.PermissionDescriptor>())), db);

    private WorkflowCancelService CancelService(AppertureDbContext db, string user) =>
        new(db, new AllowCancel(), NewUnit(db, new Principal(user)));

    private WorkflowExternalControlService ControlService(AppertureDbContext db, string user) =>
        new(db, new AllowControl(), NewUnit(db, new Principal(user)));

    private AuditedExecutionStartService StartService(AppertureDbContext db, string user) =>
        new(NewUnit(db, new Principal(user)), NewEvents(user), Workflows(db), Operations(db), new Principal(user), db);

    private WorkflowRetryService RetryService(AppertureDbContext db, string user) =>
        new(db, new AllowRetry(), Workflows(db), NewUnit(db, new Principal(user)), NewEvents(user), new Principal(user));

    private OperationItemRetryService ItemRetryService(AppertureDbContext db, string user, TestHandler handler)
    {
        var items = new SqlOperationItemStore(db);
        var attempts = new SqlOperationItemRetryAttemptStore(db);
        return new OperationItemRetryService(
            db,
            items,
            attempts,
            new OperationItemRetryReconciliationService(db, items, attempts),
            [handler],
            new AllowRetry(),
            new WorkflowStartDispatchService(
                new FixedWorkflowResolver(new WorkflowDefinition(new WorkflowDefinitionId("Test.Module.RetryWorkflow"), "Retry", "Test.Module", ["Step1"])),
                new SqlWorkflowPersistence(db),
                db),
            NewUnit(db, new Principal(user)));
    }

    private sealed class AllowCancel : IWorkflowRunCancelAuthorizationService
    {
        public ValueTask EnsureCanCancelRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class AllowControl : IWorkflowExternalControlAuthorizationService
    {
        public ValueTask EnsureCanControlRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class AllowRetry : IWorkflowRunRetryAuthorizationService
    {
        public ValueTask EnsureCanRetryRunAsync(WorkflowDefinitionId workflowDefinitionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FixedWorkflowResolver(WorkflowDefinition? definition) : IWorkflowDefinitionResolver
    {
        public ValueTask<WorkflowDefinition?> GetByIdAsync(WorkflowDefinitionId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(definition is not null && definition.Id.Value == id.Value ? definition : null);
    }

    private sealed class FixedOperationRegistry(Apperture.Core.Domain.Operations.OperationDefinition definition) : IOperationRegistryService
    {
        public ValueTask<IReadOnlyList<Apperture.Core.Domain.Operations.OperationDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Apperture.Core.Domain.Operations.OperationDefinition>>([definition]);

        public ValueTask<Apperture.Core.Domain.Operations.OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Domain.Operations.OperationDefinition?>(definition.Id.Value == operationId.Value ? definition : null);

        public ValueTask<IReadOnlyList<Apperture.Core.Domain.Operations.OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Apperture.Core.Domain.Operations.OperationDefinition>>([definition]);

        public ValueTask RegisterAsync(Apperture.Core.Domain.Operations.OperationDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    /// <summary>A module's retry handler for the test: retryable unless told otherwise, starting a fixed workflow.</summary>
    private sealed class TestHandler : IOperationItemRetryHandler
    {
        public const string Module = "Test.Module";
        public const string Kind = "test_kind";

        public bool CanRetry { get; init; } = true;

        public string ModuleId => Module;

        public string OperationKind => Kind;

        public bool Supports(OperationItem item, OperationGroup group) => group.ModuleId == Module && group.OperationKind == Kind;

        public OperationItemRetryEligibility Evaluate(OperationItem item, OperationGroup group) =>
            CanRetry
                ? new OperationItemRetryEligibility(true)
                : new OperationItemRetryEligibility(false, OperationItemRetryErrorCodes.MissingRetryMetadata, "Blocked for the test.");

        public OperationItemRetryStartPlan BuildStartPlan(OperationItemRetryBuildContext context) =>
            new(new WorkflowDefinitionId("Test.Module.RetryWorkflow"), new Dictionary<string, object?> { ["subject"] = context.Item.SubjectKey }, null, null);
    }

    /// <summary>Runs <paramref name="action"/> once, on another connection, just before the first matching command executes.</summary>
    private sealed class BeforeFirstMatchingCommand(Func<string, bool> matches, Func<Task> action) : DbCommandInterceptor
    {
        private int _fired;

        public bool Fired => Volatile.Read(ref _fired) == 1;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await FireAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await FireAsync(command);
            return result;
        }

        private async Task FireAsync(DbCommand command)
        {
            if (matches(command.CommandText) && Interlocked.Exchange(ref _fired, 1) == 0)
                await action();
        }
    }
}
