using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Notifications;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Notifications;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.NotificationSqlHarness;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 6: the notification definition writers (create, update, enable, disable, delete) as audited units against SQL
/// Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs. M7: the row of every action (actor, target, outcome,
/// category, severity, basis, the owning context, the exclusion of the rule and template text). M6 for every operation: an audit
/// insert failure leaves nothing, a lost acknowledgement is verified and reconciled, a confirmed rollback reruns once, an unknown
/// outcome is wholly committed or wholly absent, an operation lock that is not granted changes nothing; the delete is verified with
/// its target now missing. Races: writers queued on the integration row in both orders. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class NotificationDefinitionSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public NotificationDefinitionSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>A catalog of this case's own, for the cases that need an empty audit table (the M6 matrix). The audit table is append-only.</summary>
    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Aud14Notif", rcsi);

    public enum Op
    {
        Create,
        Update,
        Disable,
        Enable,
        Delete,
    }

    public static IEnumerable<object[]> Cases() =>
        from op in Enum.GetValues<Op>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    /// <summary>The M6 matrix runs on the operations that differ in kind: a create, an update and a delete (the enable and disable are an update of one flag).</summary>
    private static readonly Op[] M6Ops = [Op.Create, Op.Update, Op.Delete];

    public static IEnumerable<object[]> Ops() => M6Ops.Select(op => new object[] { op });

    public static IEnumerable<object[]> OutcomeCases() =>
        from op in M6Ops
        from committed in new[] { true, false }
        select new object[] { op, committed };

    private sealed record Arranged(
        string? DefinitionId,
        string? CreatedName,
        string IntegrationId,
        string Action,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        Action<Dictionary<string, object?>> AssertDetails);

    private static async Task<Arranged> ArrangeAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        var integrationId = await SeedIntegrationAsync(scratch);
        switch (op)
        {
            case Op.Create:
            {
                var name = "Created-" + Guid.NewGuid().ToString("N")[..8];
                return new Arranged(
                    null, name, integrationId, AuditActionNames.NotificationDefinitionCreated,
                    (db, unit) => Definitions(db, principal, unit).CreateAsync(
                        new NotificationDefinitionCreateRequest(name, "Described", integrationId, RuleJson, TemplateJson, IsEnabled: true, DefaultSeverity: "warning")).AsTask(),
                    row =>
                    {
                        Detail(row, "name").Should().Be(name);
                        Detail(row, "integrationId").Should().Be(integrationId);
                        Detail(row, "enabled").Should().Be("true");
                        Detail(row, "primaryEventType").Should().Be("platform.schedule.expected_run_missing");
                    });
            }

            case Op.Update:
            {
                var id = await SeedDefinitionAsync(scratch, integrationId, "Before update");
                return new Arranged(
                    id, null, integrationId, AuditActionNames.NotificationDefinitionUpdated,
                    (db, unit) => Definitions(db, principal, unit).UpdateAsync(
                        id, new NotificationDefinitionUpdateRequest("After update", null, null, TemplateJson.Replace("SENTINEL", "CHANGED-SENTINEL"), null, null)).AsTask(),
                    row =>
                    {
                        Detail(row, "name").Should().Be("After update");
                        Detail(row, "changedFields").Should().Contain("displayName").And.Contain("template");
                    });
            }

            case Op.Disable:
            {
                var id = await SeedDefinitionAsync(scratch, integrationId);
                return new Arranged(
                    id, null, integrationId, AuditActionNames.NotificationDefinitionDisabled,
                    (db, unit) => Definitions(db, principal, unit).SetEnabledAsync(id, false).AsTask(),
                    row => Detail(row, "integrationId").Should().Be(integrationId));
            }

            case Op.Enable:
            {
                var id = await SeedDefinitionAsync(scratch, integrationId);
                await scratch.ExecuteAsync($"UPDATE NotificationDefinitions SET IsEnabled = 0 WHERE Id = N'{id}';");
                return new Arranged(
                    id, null, integrationId, AuditActionNames.NotificationDefinitionEnabled,
                    (db, unit) => Definitions(db, principal, unit).SetEnabledAsync(id, true).AsTask(),
                    row => Detail(row, "integrationId").Should().Be(integrationId));
            }

            default:
            {
                var id = await SeedDefinitionAsync(scratch, integrationId);
                await SeedSubscriptionAsync(scratch, id, integrationId, "user-a");
                await SeedSubscriptionAsync(scratch, id, integrationId, "user-b");
                await SeedOptOutAsync(scratch, id, "user-c");
                await SeedPreferenceAsync(scratch, id, "user-d");
                return new Arranged(
                    id, null, integrationId, AuditActionNames.NotificationDefinitionDeleted,
                    (db, unit) => Definitions(db, principal, unit).DeleteAsync(id).AsTask(),
                    row =>
                    {
                        Detail(row, "subscriptionsRemoved").Should().Be("2");
                        Detail(row, "userOptOutsRemoved").Should().Be("1");
                        Detail(row, "userPreferencesRemoved").Should().Be("1");
                    });
            }
        }
    }

    // ---- M7: the committed operation ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_operation_records_one_row_attributed_to_the_administrator_and_nothing_of_the_rule_or_template(Op op, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, op, principal);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync(scratch)).Should().NotBe(before);
        var definitionId = arranged.DefinitionId
            ?? await scratch.ScalarAsync<string>($"SELECT Id FROM NotificationDefinitions WHERE DisplayName = N'{arranged.CreatedName}';");
        var rows = await AuditRowsAsync(scratch, definitionId);
        rows.Should().ContainSingle("one primary row per completed operation");
        var row = rows.Single();
        row["Action"].Should().Be(arranged.Action);
        row["OperationEventId"].Should().Be(row["EventId"], "the row is the primary row of its own operation");
        row["Actor"].Should().Be(admin);
        row["TargetType"].Should().Be(AuditTargetTypes.NotificationDefinition);
        row["TargetId"].Should().Be(definitionId);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue, "the integration's owning context");
        arranged.AssertDetails(row);
        AuditText(rows).Should().NotContain("SENTINEL-TEMPLATE-SUBJECT").And.NotContain("CHANGED-SENTINEL");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch, Op op, IPrincipalContext principal)
    {
        var arranged = await ArrangeAsync(scratch, op, principal);
        return new AuditedOperationM6Runner(scratch, () => SnapshotAsync(scratch), arranged.Action, arranged.Run, TemplateSentinel);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_nothing(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, op, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    // ---- the delete: cascade, atomicity, and a verified commit reported as success ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_delete_removes_the_definition_with_everything_that_belongs_to_it_and_nothing_else(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var doomed = await SeedDefinitionAsync(scratch, integrationId, "Doomed");
        var kept = await SeedDefinitionAsync(scratch, integrationId, "Kept");
        await SeedSubscriptionAsync(scratch, doomed, integrationId, "user-a");
        var keptSubscription = await SeedSubscriptionAsync(scratch, kept, integrationId, "user-a");
        await SeedOptOutAsync(scratch, doomed, "user-c");
        await SeedPreferenceAsync(scratch, doomed, "user-d");

        await using (var db = scratch.NewDb())
            (await Definitions(db, principal, NewUnit(db, principal)).DeleteAsync(doomed)).Should().BeTrue();

        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationDefinitions WHERE Id = N'{doomed}';")).Should().Be(0);
        foreach (var table in new[] { "NotificationSubscriptions", "NotificationSubscriptionUserOverrides", "NotificationUserPreferences" })
            (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE NotificationDefinitionId = N'{doomed}';")).Should().Be(0, table);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationSubscriptions WHERE Id = N'{keptSubscription}';")).Should().Be(1, "another definition's subscription is untouched");
        (await AuditRowsAsync(scratch, doomed)).Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.NotificationDefinitionDeleted);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Fact]
    public async Task A_delete_whose_audit_insert_fails_keeps_the_definition_and_its_subordinate_data()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, Op.Delete, principal);
        var before = await SnapshotAsync(scratch);
        var fail = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = scratch.NewDb(fail))
            await FluentActions.Awaiting(() => arranged.Run(db, NewUnit(db, principal))).Should().ThrowAsync<Exception>();

        fail.Failures.Should().BeGreaterThan(0);
        (await SnapshotAsync(scratch)).Should().Be(before, "the definition, its subscriptions, opt-outs and preferences all survive");
        (await AuditRowsAsync(scratch)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_delete_verified_after_a_lost_acknowledgement_reports_success_though_the_target_is_now_missing()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, Op.Delete, principal);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();
        var result = false;

        await using (var db = scratch.NewDb(lost, attempts))
            result = await Definitions(db, principal, NewUnit(db, principal)).DeleteAsync(arranged.DefinitionId!);

        result.Should().BeTrue("a verified commit is a delete that happened, never 'not found'");
        attempts.Started.Should().Be(1);
        (await AuditRowsAsync(scratch, arranged.DefinitionId)).Should().ContainSingle();
    }

    // ---- no-ops ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_that_change_nothing_write_nothing_and_record_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var id = await SeedDefinitionAsync(scratch, integrationId, "Unchanged");
        var current = await scratch.RowsAsync($"SELECT * FROM NotificationDefinitions WHERE Id = N'{id}';");
        var before = await SnapshotAsync(scratch);
        var auditBefore = (await AuditRowsAsync(scratch)).Count;

        await using (var db = scratch.NewDb())
        {
            var service = Definitions(db, principal, NewUnit(db, principal));
            await service.UpdateAsync(id, new NotificationDefinitionUpdateRequest("Unchanged", null, null, null, true, null));
            await service.UpdateAsync(id, new NotificationDefinitionUpdateRequest(null, null, null, null, null, null));
            await service.SetEnabledAsync(id, true);
            (await service.DeleteAsync("ndef_does_not_exist")).Should().BeFalse();
            (await service.UpdateAsync("ndef_does_not_exist", new NotificationDefinitionUpdateRequest("x", null, null, null, null, null))).Should().BeNull();
        }

        current.Should().ContainSingle();
        (await SnapshotAsync(scratch)).Should().Be(before, "not even the update timestamp moved");
        (await AuditRowsAsync(scratch)).Should().HaveCount(auditBefore);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rename_onto_another_definitions_name_is_a_refusal_and_changes_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        await SeedDefinitionAsync(scratch, integrationId, "Taken name");
        var second = await SeedDefinitionAsync(scratch, integrationId, "Second name");
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
        {
            var act = () => Definitions(db, principal, NewUnit(db, principal)).UpdateAsync(
                second, new NotificationDefinitionUpdateRequest("Taken name", null, null, null, null, null)).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*display name already exists*");
        }

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAsync(scratch, second)).Should().BeEmpty();
    }

    // ---- races: writers queued on the integration row, in both orders ----

    private sealed record Outcome<T>(T? Value, Exception? Error);

    private static async Task<Outcome<T>> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return new Outcome<T>(await Task.Run(operation), null);
        }
        catch (Exception ex)
        {
            return new Outcome<T>(default, ex);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_renames_to_the_same_name_queued_on_the_integration_leave_one_winner_and_a_refusal(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var a = await SeedDefinitionAsync(scratch, integrationId, "Racer A");
        var b = await SeedDefinitionAsync(scratch, integrationId, "Racer B");

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        Task<Outcome<NotificationDefinition?>> Rename(string id) => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Definitions(db, principal, NewUnit(db, principal)).UpdateAsync(
                id, new NotificationDefinitionUpdateRequest("Contested name", null, null, null, null, null));
        });

        var first = Rename(a);
        await holder.WaitUntilBlockedAsync(scratch, "Integrations", first);
        var second = Rename(b);
        await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(first, second), minimumWaiters: 2);
        await holder.CommitAsync();
        var one = await first;
        var two = await second;

        one.Error.Should().BeNull("the writer that arrived first wins");
        two.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("display name already exists", "a refusal, not a database error");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM NotificationDefinitions WHERE DisplayName = N'Contested name';")).Should().Be(1);
        (await AuditRowsAsync(scratch, a)).Should().ContainSingle();
        (await AuditRowsAsync(scratch, b)).Should().BeEmpty();
    }

    public static IEnumerable<object[]> Orders() =>
        from updateFirst in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { updateFirst, rcsi };

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task An_update_racing_a_delete_keeps_the_integrity_in_either_order(bool updateFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var id = await SeedDefinitionAsync(scratch, integrationId, "Raced definition");
        await SeedSubscriptionAsync(scratch, id, integrationId);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        Task<Outcome<NotificationDefinition?>> Update() => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Definitions(db, principal, NewUnit(db, principal)).UpdateAsync(id, new NotificationDefinitionUpdateRequest("Raced and renamed", null, null, null, null, null));
        });
        Task<Outcome<bool>> Delete() => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Definitions(db, principal, NewUnit(db, principal)).DeleteAsync(id);
        });

        Task<Outcome<NotificationDefinition?>> update;
        Task<Outcome<bool>> delete;
        if (updateFirst)
        {
            update = Update();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", update);
            delete = Delete();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(update, delete), minimumWaiters: 2);
        }
        else
        {
            delete = Delete();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", delete);
            update = Update();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(update, delete), minimumWaiters: 2);
        }

        await holder.CommitAsync();
        var updated = await update;
        var deleted = await delete;

        updated.Error.Should().BeNull();
        deleted.Error.Should().BeNull();
        deleted.Value.Should().BeTrue();
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationDefinitions WHERE Id = N'{id}';")).Should().Be(0);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM NotificationSubscriptions WHERE NotificationDefinitionId = N'{id}';")).Should().Be(0);
        var actions = (await AuditRowsAsync(scratch, id)).Select(r => (string)r["Action"]!).ToList();
        if (updateFirst)
        {
            updated.Value.Should().NotBeNull();
            actions.Should().Equal(AuditActionNames.NotificationDefinitionUpdated, AuditActionNames.NotificationDefinitionDeleted);
        }
        else
        {
            updated.Value.Should().BeNull("the definition was already deleted when the update got the lock");
            actions.Should().Equal(AuditActionNames.NotificationDefinitionDeleted);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_management_check_runs_against_the_integration_as_it_is_once_the_lock_is_held(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var targetContext = await OwnershipSqlSeed.SeedContextAsync(scratch, "Definition move target");
        var id = await SeedDefinitionAsync(scratch, integrationId, "Authorized again");
        var observed = new List<string>();
        var allow = new Allow
        {
            OnIntegrationUpdateCheck = integration =>
                observed.Add(scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integration}';").GetAwaiter().GetResult()),
        };

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        var update = RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            return await Definitions(db, principal, NewUnit(db, principal), allow).UpdateAsync(
                id, new NotificationDefinitionUpdateRequest("Renamed after a move", null, null, null, null, null));
        });
        await holder.WaitUntilBlockedAsync(scratch, "Integrations", update);

        // An ownership move commits while the update waits for the integration row.
        await holder.ExecuteAsync($"UPDATE Integrations SET ResourceContextId = N'{targetContext}' WHERE Id = N'{integrationId}';");
        await holder.CommitAsync();
        var outcome = await update;

        outcome.Error.Should().BeNull();
        observed.Should().HaveCount(2).And.ContainInOrder(ResourceContextIds.SharedValue, targetContext);
        var row = (await AuditRowsAsync(scratch, id)).Single();
        row["ResourceContextId"].Should().Be(targetContext, "the row names the owner the unit locked, not the one it first read");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_management_check_that_fails_under_the_lock_rolls_everything_back(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var id = await SeedDefinitionAsync(scratch, integrationId, "Denied under the lock");
        var before = await SnapshotAsync(scratch);
        var allow = new Allow { DenyIntegrationUpdateAfterTheFirst = true };

        await using (var db = scratch.NewDb())
        {
            var service = Definitions(db, principal, NewUnit(db, principal), allow);
            await FluentActions.Awaiting(() => service.UpdateAsync(id, new NotificationDefinitionUpdateRequest("Never", null, null, null, null, null)).AsTask())
                .Should().ThrowAsync<UnauthorizedAccessException>();
        }

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAsync(scratch, id)).Should().BeEmpty();
    }
}
