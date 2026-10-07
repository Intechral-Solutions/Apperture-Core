using System.Text.Json;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The audited unit of work against SQL Server (audit hardening design gate §13; A1.1-1, -2, -3, -9, -10, -14 to -18;
/// M6): the change and its rows commit together; after an uncertain commit the outcome is verified on a fresh connection
/// under the operation lock before anything reruns; only the primary <c>EventId</c> is a prior-success candidate; and an
/// outcome that cannot be established is reported as unknown. Faults are injected with EF interceptors. Fails, never
/// skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditRecorderSqlServerTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private AuditSqlScratch _db = null!;

    public AuditRecorderSqlServerTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _db = await AuditSqlScratch.CreateAsync("Apperture_Aud11Recorder_");
        await _db.ExecuteAsync("CREATE TABLE dbo.AuditHarnessItems (Id nvarchar(64) NOT NULL PRIMARY KEY, Name nvarchar(256) NULL);");
    }

    public async Task DisposeAsync()
    {
        _output.WriteLine(_db.ExecutionMarker);
        await _db.DisposeAsync();
    }

    // ---- A1.1-1, A1.1-3, A1.1-9: a complete recorder row with structured details ----

    [Fact]
    public async Task A_rename_commits_with_one_complete_recorder_row_holding_structured_previous_and_new_names()
    {
        var (admin, adminName) = await AddUserAsync("admin");
        var (target, targetName) = await AddUserAsync("alice");
        var newName = NewUsername("renamed");

        await using var db = _db.NewDb();
        var principal = new Principal(admin);
        var result = await RenameService(db, principal, NewUnit(db, principal))
            .RenameUsernameAsync(new UserId(target), newName);

        result.Should().Be(new UsernameRenameResult(UsernameChangeOutcome.Assigned, targetName, newName));
        var row = (await RowsForTargetAsync(target)).Should().ContainSingle().Which;
        row["Producer"].Should().Be("recorder");
        row["ClassificationState"].Should().Be("complete");
        row["ActorKind"].Should().Be("user");
        row["Actor"].Should().Be(admin);
        row["ActorUserId"].Should().Be(admin);
        row["ActorUsername"].Should().Be(adminName, "read in the recording transaction");
        row["ActorAttribution"].Should().Be("recorded");
        row["TargetType"].Should().Be("user");
        row["TargetId"].Should().Be(target);
        row["Category"].Should().Be("identity");
        row["Severity"].Should().Be("elevated");
        row["Visibility"].Should().Be("default");
        row["Basis"].Should().Be("change");
        row["Outcome"].Should().Be("succeeded");
        row["OutcomeCode"].Should().Be("succeeded");
        row["OutcomeProvenance"].Should().Be("recorded");
        row["AttributionIssue"].Should().BeNull();
        row["DetailsFormat"].Should().Be("json/1");
        row["EventId"].Should().Be(row["OperationEventId"], "a single-row operation's row is its primary row");

        var details = (string)row["Details"]!;
        details.Should().NotContain("Resource=");
        using var json = JsonDocument.Parse(details);
        json.RootElement.GetProperty("previousUsername").GetString().Should().Be(targetName);
        json.RootElement.GetProperty("newUsername").GetString().Should().Be(newName);
        json.RootElement.GetProperty("change").GetString().Should().Be("assigned");
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["userId", "previousUsername", "newUsername", "change"]);
        _output.WriteLine(_db.ExecutionMarker);
    }

    // ---- A1.1-2: event-time username from the transaction, never the session claim ----

    [Fact]
    public async Task The_actor_username_is_read_in_the_transaction_and_a_later_rename_never_changes_the_row()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (other, _) = await AddUserAsync("other");
        var (target, _) = await AddUserAsync("bob");
        var adminRenamed = NewUsername("admin.new");

        // Another administrator renames the acting administrator while the acting administrator's session (and its
        // username claim) stays alive.
        await using (var db = _db.NewDb())
        {
            var principal = new Principal(other);
            await RenameService(db, principal, NewUnit(db, principal)).RenameUsernameAsync(new UserId(admin), adminRenamed);
        }

        await using (var db = _db.NewDb())
        {
            var stale = new Principal(admin, staleSessionName: "admin.claim.from.sign.in");
            await RenameService(db, stale, NewUnit(db, stale)).RenameUsernameAsync(new UserId(target), NewUsername("bob.new"));
        }

        var row = (await RowsForTargetAsync(target)).Single();
        row["ActorUsername"].Should().Be(adminRenamed, "the recorder reads Users.Username in its own transaction");

        // Renaming the actor afterwards leaves the recorded evidence untouched.
        await using (var db = _db.NewDb())
        {
            var principal = new Principal(other);
            await RenameService(db, principal, NewUnit(db, principal)).RenameUsernameAsync(new UserId(admin), NewUsername("admin.later"));
        }

        (await RowsForTargetAsync(target)).Single()["ActorUsername"].Should().Be(adminRenamed);
    }

    [Fact]
    public async Task A_self_rename_records_the_name_the_transaction_sees_after_its_own_rename()
    {
        var (admin, oldName) = await AddUserAsync("self");
        var newName = NewUsername("self.new");

        await using var db = _db.NewDb();
        var principal = new Principal(admin);
        await RenameService(db, principal, NewUnit(db, principal)).RenameUsernameAsync(new UserId(admin), newName);

        var row = (await RowsForTargetAsync(admin)).Single();
        row["ActorUsername"].Should().Be(newName);
        ((string)row["Details"]!).Should().Contain(oldName).And.Contain(newName);
    }

    // ---- A1.1-10: audit insert failure leaves no change committed ----

    [Fact]
    public async Task When_the_audit_insert_fails_the_rename_rolls_back_with_it()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (target, targetName) = await AddUserAsync("carol");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using var db = _db.NewDb(failAudit);
        var principal = new Principal(admin);
        var act = () => RenameService(db, principal, NewUnit(db, principal)).RenameUsernameAsync(new UserId(target), NewUsername("carol.new")).AsTask();

        await act.Should().ThrowAsync<Exception>();
        failAudit.Failures.Should().BeGreaterThan(0, "the fault was injected at the audit insert");
        (await _db.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{target}';")).Should().Be(targetName);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM UsernameLedger WHERE UserId = N'{target}';")).Should().Be(1);
        (await _db.ScalarAsync<string>($"SELECT ExternalId FROM IdentityLinks WHERE UserId = N'{target}' AND Source = 1;")).Should().Be(targetName);
        (await RowsForTargetAsync(target)).Should().BeEmpty();
    }

    // ---- A1.1-14: lost acknowledgement, then a rename that would appear unchanged ----

    [Fact]
    public async Task A_rename_whose_commit_acknowledgement_is_lost_is_verified_and_reported_from_its_recorded_names()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (target, targetName) = await AddUserAsync("dave");
        var newName = NewUsername("dave.new");
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using var db = _db.NewDb(lost, attempts);
        var principal = new Principal(admin);
        var result = await RenameService(db, principal, NewUnit(db, principal)).RenameUsernameAsync(new UserId(target), newName);

        result.Should().Be(new UsernameRenameResult(UsernameChangeOutcome.Assigned, targetName, newName),
            "verification confirmed the commit, so the result is rebuilt from the row instead of rerunning (a rerun would see 'unchanged')");
        attempts.Started.Should().Be(1, "no attempt reruns after a verified commit");
        (await RowsForTargetAsync(target)).Should().ContainSingle();
        (await _db.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{target}';")).Should().Be(newName);
    }

    // ---- A1.1-15: lost acknowledgement, then a delete whose target would appear missing ----

    [Fact]
    public async Task A_delete_whose_commit_acknowledgement_is_lost_is_reported_as_done_not_as_not_found()
    {
        var item = await AddItemAsync();
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using var db = _db.NewDb(lost, attempts);
        var result = await DeleteItemAsync(NewUnit(db, null), db, item);

        result.Should().Be("deleted");
        attempts.Started.Should().Be(1);
        (await _db.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditHarnessItems WHERE Id = N'{item}';")).Should().Be(0);
        (await HarnessRowsAsync(item)).Should().ContainSingle();

        // What a blind rerun would have reported: the target appears missing.
        await using var again = _db.NewDb();
        var rerun = () => DeleteItemAsync(NewUnit(again, null), again, item);
        await rerun.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---- A1.1-16: several rows in one transaction ----

    [Fact]
    public async Task A_multi_row_operation_with_a_lost_acknowledgement_verifies_by_its_primary_row_and_keeps_exactly_its_rows()
    {
        var item = await AddItemAsync();
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        await using var db = _db.NewDb(lost, attempts);
        var unit = NewUnit(db, null);
        await ChangeItemAsync(unit, db, item, "renamed");

        attempts.Started.Should().Be(1);
        var rows = await HarnessRowsAsync(item);
        rows.Should().HaveCount(2);
        var primary = rows.Single(r => (string)r["Action"]! == HarnessPrimary.Action);
        var secondary = rows.Single(r => (string)r["Action"]! == HarnessSecondary.Action);
        secondary["OperationEventId"].Should().Be(primary["EventId"], "every row carries the primary's id as its operation id");
        secondary["EventId"].Should().NotBe(primary["EventId"]);
    }

    [Fact]
    public async Task A_multi_row_operation_whose_commit_rolled_back_reruns_once_and_commits_one_set_of_rows()
    {
        var item = await AddItemAsync();
        var rolledBack = new RolledBackCommit();
        var attempts = new AttemptCounter();

        await using var db = _db.NewDb(rolledBack, attempts);
        await ChangeItemAsync(NewUnit(db, null), db, item, "renamed");

        attempts.Started.Should().Be(2, "verification confirmed the rollback, so one rerun was allowed");
        (await HarnessRowsAsync(item)).Should().HaveCount(2, "exactly one set of rows");
        (await _db.ScalarAsync<string>($"SELECT Name FROM AuditHarnessItems WHERE Id = N'{item}';")).Should().Be("renamed");
    }

    // ---- A1.1-17: unrelated unique violations keep their meaning; only the primary EventId is a candidate ----

    [Fact]
    public async Task A_rename_to_another_users_name_is_username_taken_and_never_a_prior_success()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (target, targetName) = await AddUserAsync("erin");
        var (_, otherName) = await AddUserAsync("frank");
        var attempts = new AttemptCounter();

        await using var db = _db.NewDb(attempts);
        var principal = new Principal(admin);
        var act = () => RenameService(db, principal, NewUnit(db, principal)).RenameUsernameAsync(new UserId(target), otherName).AsTask();

        (await act.Should().ThrowAsync<UsernameException>()).Which.Code.Should().Be(UsernameErrorCodes.Taken);
        attempts.Started.Should().Be(1);
        (await _db.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{target}';")).Should().Be(targetName);
        (await RowsForTargetAsync(target)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_duplicate_on_the_primary_event_id_is_success_only_after_verification_confirms_the_matching_event()
    {
        var item = await AddItemAsync();
        var operationId = Guid.NewGuid().ToString("N");
        // The operation's primary row already committed (an earlier attempt whose acknowledgement was lost).
        await InsertRecorderRowAsync(operationId, operationId, HarnessPrimary.Action, item);

        await using var db = _db.NewDb();
        var result = await ChangeItemAsync(NewUnit(db, null, eventIds: new FixedEventIds(operationId)), db, item, "second-attempt");

        result.Should().Be("reconstructed");
        (await _db.ScalarAsync<string>($"SELECT Name FROM AuditHarnessItems WHERE Id = N'{item}';"))
            .Should().BeNull("the duplicate attempt rolled back; nothing ran twice");
        (await HarnessRowsAsync(item)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_duplicate_primary_event_id_that_records_another_action_is_outcome_unknown()
    {
        var item = await AddItemAsync();
        var operationId = Guid.NewGuid().ToString("N");
        await InsertRecorderRowAsync(operationId, operationId, HarnessDeleted.Action, item);

        await using var db = _db.NewDb();
        var act = () => ChangeItemAsync(NewUnit(db, null, eventIds: new FixedEventIds(operationId)), db, item, "x");

        (await act.Should().ThrowAsync<AuditOutcomeUnknownException>()).Which.OperationEventId.Should().Be(operationId);
    }

    [Fact]
    public async Task A_duplicate_on_a_secondary_event_id_is_an_internal_error_not_a_success()
    {
        var item = await AddItemAsync();
        var secondaryId = Guid.NewGuid().ToString("N");
        await InsertRecorderRowAsync(secondaryId, secondaryId, HarnessSecondary.Action, "elsewhere");

        await using var db = _db.NewDb();
        var act = () => ChangeItemAsync(NewUnit(db, null, eventIds: new FixedEventIds(Guid.NewGuid().ToString("N"), secondaryId)), db, item, "x");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*secondary audit event id*");
        (await HarnessRowsAsync(item)).Should().BeEmpty();
    }

    // ---- A1.1-18: lock not granted, or verification cannot run: outcome unknown, no rerun ----

    [Fact]
    public async Task When_the_operation_lock_is_not_granted_to_verification_the_outcome_is_unknown_and_nothing_reruns()
    {
        var item = await AddItemAsync();
        var operationId = Guid.NewGuid().ToString("N");
        HeldLock? held = null;
        var lost = new LostCommitAcknowledgement
        {
            // Another session holds the operation lock when verification asks for it (an earlier attempt still in flight).
            AfterCommit = async () => held = await HeldLock.TakeAsync(_db.ConnectionString, "apperture.audit-op." + operationId),
        };
        var attempts = new AttemptCounter();

        try
        {
            await using var db = _db.NewDb(lost, attempts);
            var act = () => ChangeItemAsync(NewUnit(db, null, eventIds: new FixedEventIds(operationId)), db, item, "x");

            (await act.Should().ThrowAsync<AuditOutcomeUnknownException>()).Which.OperationEventId.Should().Be(operationId);
            attempts.Started.Should().Be(1);
        }
        finally
        {
            if (held is not null)
                await held.DisposeAsync();
        }

        (await HarnessRowsAsync(item)).Should().HaveCount(2, "the commit had in fact happened: unknown never claims a rollback");
    }

    [Fact]
    public async Task When_the_verification_connection_fails_the_outcome_is_unknown_and_nothing_reruns()
    {
        var item = await AddItemAsync();
        var verification = new UnreachableVerification();
        var attempts = new AttemptCounter();

        await using var db = _db.NewDb(new RolledBackCommit(), attempts);
        var act = () => ChangeItemAsync(NewUnit(db, null, verification: verification), db, item, "x");

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        verification.Calls.Should().Be(1);
        attempts.Started.Should().Be(1, "even a commit that in fact rolled back is not rerun unless verification proves it");
    }

    [Fact]
    public async Task When_an_attempt_cannot_take_its_own_operation_lock_the_outcome_is_unknown()
    {
        var item = await AddItemAsync();
        var operationId = Guid.NewGuid().ToString("N");
        await using var held = await HeldLock.TakeAsync(_db.ConnectionString, "apperture.audit-op." + operationId);

        await using var db = _db.NewDb();
        var act = () => ChangeItemAsync(NewUnit(db, null, eventIds: new FixedEventIds(operationId)), db, item, "x");

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        (await HarnessRowsAsync(item)).Should().BeEmpty();
    }

    // ---- Unit-of-work preconditions and the interceptor (§13.4) ----

    [Fact]
    public async Task An_attempt_that_stages_no_primary_row_commits_nothing()
    {
        var item = await AddItemAsync();
        await using var db = _db.NewDb();

        var act = () => NewUnit(db, null).ExecuteAsync(
            new AuditedOperation(HarnessPrimary.Action, AuditActor.System()),
            async (tx, ct) =>
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditHarnessItems SET Name = N'unaudited' WHERE Id = {item};", ct);
                return 0;
            },
            (_, _) => Task.FromResult(0));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*staged none*");
        (await _db.ScalarAsync<string>($"SELECT Name FROM AuditHarnessItems WHERE Id = N'{item}';")).Should().BeNull();
    }

    [Fact]
    public async Task Staging_refuses_undeclared_and_non_W1_actions_and_staging_outside_the_transaction()
    {
        var item = await AddItemAsync();
        await using var db = _db.NewDb();
        IAuditedTransaction? escaped = null;

        var nonW1 = () => NewUnit(db, null).ExecuteAsync(
            new AuditedOperation(HarnessPrimary.Action, AuditActor.System()),
            async (tx, ct) =>
            {
                await tx.StageAsync(HarnessEvent(HarnessAttempt, item), ct);
                return 0;
            },
            (_, _) => Task.FromResult(0));
        await nonW1.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a W1 action*");

        await NewUnit(db, null).ExecuteAsync(
            new AuditedOperation(HarnessPrimary.Action, AuditActor.System()),
            async (tx, ct) =>
            {
                escaped = tx;
                await tx.StageAsync(HarnessEvent(HarnessPrimary, item), ct);
                return 0;
            },
            (_, _) => Task.FromResult(0));
        var late = () => escaped!.StageAsync(HarnessEvent(HarnessSecondary, item)).AsTask();
        await late.Should().ThrowAsync<InvalidOperationException>().WithMessage("*transaction is current*");

        var undeclared = () => new SqlAuditedUnitOfWork(db).ExecuteAsync(
            new AuditedOperation(HarnessPrimary.Action, AuditActor.System()),
            (_, _) => Task.FromResult(0),
            (_, _) => Task.FromResult(0));
        await undeclared.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not declared*");
    }

    [Fact]
    public async Task A_recorder_row_saved_without_a_transaction_is_rejected_by_the_interceptor()
    {
        await using var db = _db.NewDb();
        var id = Guid.NewGuid().ToString("N");
        db.AuditLogs.Add(new AuditLogEntity
        {
            At = DateTimeOffset.UtcNow, Actor = "system", Action = HarnessPrimary.Action, Details = "{}",
            EventId = id, OperationEventId = id, Producer = AuditProducers.Recorder, DetailsFormat = AuditDetailsFormats.Json1,
        });

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inside its unit of work*");
    }

    [Fact]
    public async Task A_repository_joins_the_unit_transaction_so_a_later_failure_rolls_its_change_back()
    {
        var (admin, _) = await AddUserAsync("admin");
        var (target, targetName) = await AddUserAsync("gina");

        await using var db = _db.NewDb();
        var repository = new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance);
        var act = () => NewUnit(db, new Principal(admin)).ExecuteAsync(
            new AuditedOperation(AuditActionNames.UsernameChanged, AuditActor.CurrentPrincipal),
            async (tx, ct) =>
            {
                await repository.ChangeUsernameAsync(new UserId(target), targetName, NewUsername("gina.new"), UsernameRetiredReasons.Rename, cancellationToken: ct);
                (await db.Database.SqlQueryRaw<string>("SELECT Username AS Value FROM Users WHERE Id = {0}", target).SingleAsync(ct))
                    .Should().NotBe(targetName, "the change is visible inside the unit's transaction");
                throw new InvalidOperationException("a later step fails");
            },
            (_, _) => Task.FromResult(0));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("a later step fails");
        (await _db.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{target}';"))
            .Should().Be(targetName, "the repository joined the unit's transaction instead of committing its own");
    }

    // ---- WriteAsync / TryWriteAsync (W2-W4) ----

    [Fact]
    public async Task Single_row_writes_commit_on_their_own_context_and_never_flush_the_callers_pending_changes()
    {
        var (admin, adminName) = await AddUserAsync("admin");
        var item = Guid.NewGuid().ToString("N");
        await using var caller = _db.NewDb();
        var pending = new AuditLogEntity { At = DateTimeOffset.UtcNow, Actor = "x", Action = "pending.change" };
        caller.AuditLogs.Add(pending);

        var writer = new SqlAuditEventWriter(_db.Options(), new Principal(admin), HarnessCatalog, null, null, null);
        await writer.WriteAsync(HarnessEvent(HarnessAttempt, item), AuditActor.CurrentPrincipal);
        (await writer.TryWriteAsync(HarnessEvent(HarnessTelemetry, item), AuditActor.System("scheduler"))).Should().BeTrue();

        caller.Entry(pending).State.Should().Be(EntityState.Added, "the caller's change was not flushed");
        var rows = await HarnessRowsAsync(item);
        rows.Should().HaveCount(2);
        rows.Single(r => (string)r["Action"]! == HarnessAttempt.Action)["ActorUsername"].Should().Be(adminName);
        rows.Single(r => (string)r["Action"]! == HarnessTelemetry.Action)["Actor"].Should().Be("system:scheduler");

        var wrongPath = () => writer.WriteAsync(HarnessEvent(HarnessPrimary, item), AuditActor.System());
        await wrongPath.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not carry its guarantee*");
    }

    [Fact]
    public async Task A_single_row_write_whose_row_already_exists_after_a_lost_acknowledgement_succeeds_once()
    {
        var item = Guid.NewGuid().ToString("N");
        var eventId = Guid.NewGuid().ToString("N");
        await InsertRecorderRowAsync(eventId, eventId, HarnessAttempt.Action, item);

        var writer = new SqlAuditEventWriter(_db.Options(), null, HarnessCatalog, null, null, new FixedEventIds(eventId));
        await writer.WriteAsync(HarnessEvent(HarnessAttempt, item), AuditActor.Anonymous);

        (await HarnessRowsAsync(item)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_telemetry_write_that_fails_is_logged_and_reported_false()
    {
        var item = Guid.NewGuid().ToString("N");
        var failing = _db.Options(new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal)));
        var writer = new SqlAuditEventWriter(failing, null, HarnessCatalog, null, null, null);

        (await writer.TryWriteAsync(HarnessEvent(HarnessTelemetry, item), AuditActor.Worker)).Should().BeFalse();
        (await HarnessRowsAsync(item)).Should().BeEmpty();
    }

    // ---- helpers ----

    private async Task<(string Id, string Username)> AddUserAsync(string stem)
    {
        var id = NewUserId();
        var username = NewUsername(stem);
        await _db.AddUserAsync(id, username);
        return (id, username);
    }

    private async Task<string> AddItemAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        await _db.ExecuteAsync($"INSERT INTO AuditHarnessItems (Id) VALUES (N'{id}');");
        return id;
    }

    /// <summary>A two-row W1 operation: changes the item, stages the primary and a secondary row.</summary>
    private static Task<string> ChangeItemAsync(SqlAuditedUnitOfWork unit, AppertureDbContext db, string item, string name) =>
        unit.ExecuteAsync(
            new AuditedOperation(HarnessPrimary.Action, AuditActor.System()),
            async (tx, ct) =>
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuditHarnessItems SET Name = {name} WHERE Id = {item};", ct);
                await tx.StageAsync(HarnessEvent(HarnessPrimary, item, name), ct);
                await tx.StageAsync(HarnessEvent(HarnessSecondary, item, name), ct);
                return "changed";
            },
            (_, _) => Task.FromResult("reconstructed"));

    /// <summary>The delete harness: not found when the item is missing; success reconstructed from the row.</summary>
    private static Task<string> DeleteItemAsync(SqlAuditedUnitOfWork unit, AppertureDbContext db, string item) =>
        unit.ExecuteAsync(
            new AuditedOperation(HarnessDeleted.Action, AuditActor.System()),
            async (tx, ct) =>
            {
                var deleted = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AuditHarnessItems WHERE Id = {item};", ct);
                if (deleted == 0)
                    throw new KeyNotFoundException($"Item {item} not found.");
                await tx.StageAsync(HarnessEvent(HarnessDeleted, item), ct);
                return "deleted";
            },
            (_, _) => Task.FromResult("deleted"));

    private Task InsertRecorderRowAsync(string eventId, string operationEventId, string action, string item) =>
        _db.ExecuteAsync($$"""
            INSERT INTO AuditLogs (At, Actor, Action, Details, Outcome, EventId, OperationEventId, Producer, DetailsFormat,
                ClassificationState, ActorKind, ActorAttribution, Category, Severity, Visibility, Basis, OutcomeCode, OutcomeProvenance)
            VALUES (SYSDATETIMEOFFSET(), N'system', N'{{action}}', N'{"itemId":"{{item}}"}', N'succeeded', N'{{eventId}}', N'{{operationEventId}}',
                N'recorder', N'json/1', N'complete', N'system', N'recorded', N'configuration', N'standard', N'default', N'change',
                N'succeeded', N'recorded');
            """);

    private Task<List<Dictionary<string, object?>>> RowsForTargetAsync(string userId) =>
        _db.RowsAsync($"SELECT * FROM AuditLogs WHERE TargetType = N'user' AND TargetId = N'{userId}' ORDER BY Id;");

    private Task<List<Dictionary<string, object?>>> HarnessRowsAsync(string item) =>
        _db.RowsAsync($"SELECT * FROM AuditLogs WHERE Action LIKE N'harness.%' AND Details LIKE N'%\"{item}\"%' ORDER BY Id;");
}
