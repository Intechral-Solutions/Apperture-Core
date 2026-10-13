using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Security.Secrets.Providers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 7: the attention routing writers (instance default update, integration override create, update and clear) as
/// audited units against SQL Server on locking READ COMMITTED and READ_COMMITTED_SNAPSHOT catalogs. M7: the row of every action
/// (actor, target, outcome, category, severity, basis, visibility, the owning context of an override, details that name only the
/// condition and the fields that changed; team ids and durations never reach a row). A request that changes nothing writes and
/// records nothing. An ownership move that commits while a write waits for the integration row is seen by the re-run
/// authorization, and a caller who lost <c>integration.update</c> through the move writes nothing. An integration delete and an
/// override create queued on the integration row in both orders leave either no override or an override the delete refuses to
/// orphan. M6 for every writer: an audit insert failure leaves nothing, a lost acknowledgement is verified and reconciled, a
/// confirmed rollback reruns once, an unknown outcome is wholly committed or wholly absent, an operation lock that is not granted
/// changes nothing. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AttentionRoutingSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Key = AttentionConditionKeys.ExpectedRunMissing;

    /// <summary>A team id and durations that must never reach an audit row.</summary>
    private const string PrimarySentinel = "team-sentinel-primary-do-not-record";

    private const string EscalationSentinel = "team-sentinel-escalation-do-not-record";
    private const int SentinelSeconds = 91234;

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public AttentionRoutingSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>A catalog of this case's own, for the cases that need an empty audit table (the M6 matrix). The audit table is append-only.</summary>
    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Aud14Rout", rcsi);

    public enum Op
    {
        Default,
        Create,
        Update,
        Clear,
    }

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    public static IEnumerable<object[]> Cases() =>
        from op in Enum.GetValues<Op>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    public static IEnumerable<object[]> Ops() => Enum.GetValues<Op>().Select(op => new object[] { op });

    public static IEnumerable<object[]> OutcomeCases() =>
        from op in Enum.GetValues<Op>()
        from committed in new[] { true, false }
        select new object[] { op, committed };

    public static IEnumerable<object[]> MoveCases() =>
        from op in new[] { Op.Create, Op.Update, Op.Clear }
        from loseAccess in new[] { false, true }
        from rcsi in new[] { false, true }
        select new object[] { op, loseAccess, rcsi };

    public static IEnumerable<object[]> DeleteRaces() =>
        from deleteFirst in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { deleteFirst, rcsi };

    // ---- the service over a real catalog ----

    private static AttentionRoutingManagementService Routing(AppertureDbContext db, IAuditedUnitOfWork unit) =>
        new(db, new SqlIntegrationRepository(db), null!, unit);

    private static AttentionRoutingUpsertCommand Command(
        bool enabled = true,
        bool sendResolved = true,
        bool suppressReminders = false,
        bool suppressEscalation = false,
        TimeSpan? escalationAfter = null,
        TimeSpan? reminderInterval = null,
        string? primaryTeam = null,
        string? escalationTeam = null) =>
        new(enabled, sendResolved, suppressReminders, suppressEscalation, escalationAfter ?? TimeSpan.FromMinutes(30), reminderInterval ?? TimeSpan.FromMinutes(10), primaryTeam, escalationTeam);

    /// <summary>The seeded override and the command that equals it.</summary>
    private static AttentionRoutingUpsertCommand Baseline() => Command();

    private static string TeamSql(string id) =>
        $"""
        IF NOT EXISTS (SELECT 1 FROM OperationalTeams WHERE Id = N'{id}')
          INSERT INTO OperationalTeams (Id, Name, Description, IsEnabled, CreatedAtUtc, UpdatedAtUtc)
          VALUES (N'{id}', N'Sample Team {id[^12..]}', NULL, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
        """;

    private static async Task SeedTeamsAsync(AuditSqlScratch scratch)
    {
        await scratch.ExecuteAsync(TeamSql(PrimarySentinel));
        await scratch.ExecuteAsync(TeamSql(EscalationSentinel));
    }

    /// <summary>An override straight into the table (the audit table is append-only, so a fixture writes no audit row).</summary>
    private static Task SeedOverrideAsync(AuditSqlScratch scratch, string integrationId, string conditionKey = Key) =>
        scratch.ExecuteAsync(
            $"""
            INSERT INTO IntegrationAttentionRoutings
              (Id, IntegrationId, ConditionKey, IsEnabled, SendResolvedNotification, SuppressRemindersWhenAcknowledged,
               SuppressEscalationWhenAcknowledged, EscalationAfterTicks, ReminderIntervalTicks, PrimaryOperationalTeamId, EscalationOperationalTeamId)
            VALUES
              (N'attnrt_{Guid.NewGuid():N}', N'{integrationId}', N'{conditionKey}', 1, 1, 0, 0,
               {TimeSpan.FromMinutes(30).Ticks}, {TimeSpan.FromMinutes(10).Ticks}, NULL, NULL);
            """);

    /// <summary>The instance default of the platform's condition, put back to a known baseline (the shared catalogs keep it between cases).</summary>
    private static Task ResetDefaultAsync(AuditSqlScratch scratch) =>
        scratch.ExecuteAsync(
            $"""
            UPDATE InstanceAttentionRoutings
            SET IsEnabled = 1, SendResolvedNotification = 1, SuppressRemindersWhenAcknowledged = 1, SuppressEscalationWhenAcknowledged = 1,
                EscalationAfterTicks = NULL, ReminderIntervalTicks = NULL, PrimaryOperationalTeamId = NULL, EscalationOperationalTeamId = NULL
            WHERE Id = N'{InstanceAttentionRoutingDefaults.RowId}' AND ConditionKey = N'{Key}';
            """);

    private static AttentionRoutingUpsertCommand DefaultBaseline() =>
        new(true, true, true, true, null, null, null, null);

    private static Task<string> SeedIntegrationAsync(AuditSqlScratch scratch) =>
        OwnershipSqlSeed.SeedIntegrationAsync(scratch, ResourceContextIds.SharedValue);

    // ---- observation ----

    private static string RowText(IEnumerable<Dictionary<string, object?>> rows) =>
        JsonSerializer.Serialize(rows.Select(r => r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString())));

    /// <summary>Every routing row (instance and integration), as one comparable text.</summary>
    private static async Task<string> SnapshotAsync(AuditSqlScratch scratch) =>
        RowText(await scratch.RowsAsync("SELECT * FROM InstanceAttentionRoutings ORDER BY Id;"))
        + "#" + RowText(await scratch.RowsAsync("SELECT * FROM IntegrationAttentionRoutings ORDER BY Id;"));

    private static Task<List<Dictionary<string, object?>>> OverrideRowsAsync(AuditSqlScratch scratch, string integrationId) =>
        scratch.RowsAsync($"SELECT * FROM IntegrationAttentionRoutings WHERE IntegrationId = N'{integrationId}' ORDER BY Id;");

    private static Task<List<Dictionary<string, object?>>> DefaultRowsAsync(AuditSqlScratch scratch) =>
        scratch.RowsAsync($"SELECT * FROM InstanceAttentionRoutings WHERE Id = N'{InstanceAttentionRoutingDefaults.RowId}' AND ConditionKey = N'{Key}';");

    private static Task<long> LastAuditIdAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<long>("SELECT ISNULL(MAX(Id), 0) FROM AuditLogs;");

    private static Task<List<Dictionary<string, object?>>> AuditRowsAfterAsync(AuditSqlScratch scratch, long afterId) =>
        scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE Id > {afterId} ORDER BY Id;");

    private static string Detail(Dictionary<string, object?> row, string name) =>
        ConnectionSqlHarness.Detail(row, name) ?? string.Empty;

    private static IReadOnlyList<string> DetailList(Dictionary<string, object?> row, string name)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private static IReadOnlyList<string> DetailNames(Dictionary<string, object?> row)
    {
        using var details = JsonDocument.Parse((string)row["Details"]!);
        return details.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }

    private static Task<(string Admin, IPrincipalContext Principal)> AdminAsync(AuditSqlScratch scratch) =>
        ConnectionSqlHarness.AdminAsync(scratch);

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

    // ---- arranging one operation ----

    private sealed record Arranged(
        string? IntegrationId,
        string Action,
        string TargetType,
        string TargetId,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        IReadOnlyList<string> ExpectedDetailNames,
        IReadOnlyList<string>? ExpectedChanged);

    private static async Task<Arranged> ArrangeAsync(AuditSqlScratch scratch, Op op)
    {
        await SeedTeamsAsync(scratch);
        switch (op)
        {
            case Op.Default:
                await ResetDefaultAsync(scratch);
                return new Arranged(
                    null, AuditActionNames.AttentionRoutingDefaultUpdated, AuditTargetTypes.Instance, AuditEvents.InstanceTargetId,
                    (db, unit) => Routing(db, unit).UpsertInstanceDefaultRoutingAsync(
                        Key,
                        DefaultBaseline() with
                        {
                            IsEnabled = false,
                            ReminderInterval = TimeSpan.FromSeconds(SentinelSeconds),
                            PrimaryOperationalTeamId = PrimarySentinel,
                        }).AsTask(),
                    ["conditionKey", "changedFields"],
                    ["enabled", "reminderInterval", "primaryTeam"]);

            case Op.Create:
            {
                var integrationId = await SeedIntegrationAsync(scratch);
                return new Arranged(
                    integrationId, AuditActionNames.AttentionRoutingOverrideCreated, AuditTargetTypes.Integration, integrationId,
                    (db, unit) => Routing(db, unit).UpsertIntegrationRoutingAsync(
                        new IntegrationId(integrationId), Key,
                        Command(escalationAfter: TimeSpan.FromSeconds(SentinelSeconds), primaryTeam: PrimarySentinel, escalationTeam: EscalationSentinel)).AsTask(),
                    ["name", "conditionKey"],
                    null);
            }

            case Op.Update:
            {
                var integrationId = await SeedIntegrationAsync(scratch);
                await SeedOverrideAsync(scratch, integrationId);
                return new Arranged(
                    integrationId, AuditActionNames.AttentionRoutingOverrideUpdated, AuditTargetTypes.Integration, integrationId,
                    (db, unit) => Routing(db, unit).UpsertIntegrationRoutingAsync(
                        new IntegrationId(integrationId), Key,
                        Baseline() with
                        {
                            IsEnabled = false,
                            EscalationAfter = TimeSpan.FromSeconds(SentinelSeconds),
                            PrimaryOperationalTeamId = PrimarySentinel,
                        }).AsTask(),
                    ["name", "conditionKey", "changedFields"],
                    ["enabled", "escalationAfter", "primaryTeam"]);
            }

            default:
            {
                var integrationId = await SeedIntegrationAsync(scratch);
                await SeedOverrideAsync(scratch, integrationId);
                return new Arranged(
                    integrationId, AuditActionNames.AttentionRoutingOverrideCleared, AuditTargetTypes.Integration, integrationId,
                    async (db, unit) => (await Routing(db, unit).ClearIntegrationRoutingAsync(new IntegrationId(integrationId), Key)).Should().BeTrue(),
                    ["name", "conditionKey"],
                    null);
            }
        }
    }

    // ---- M7: the committed operation ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_operation_records_one_row_attributed_to_the_administrator_and_only_the_fields_that_changed(Op op, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, op);
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync(scratch)).Should().NotBe(before);
        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        var row = rows.Should().ContainSingle("one primary row per completed operation").Subject;
        row["Action"].Should().Be(arranged.Action);
        row["OperationEventId"].Should().Be(row["EventId"], "the row is the primary row of its own operation");
        row["Actor"].Should().Be(admin);
        row["TargetType"].Should().Be(arranged.TargetType);
        row["TargetId"].Should().Be(arranged.TargetId);
        row["OutcomeCode"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["Category"].Should().Be(AuditCategories.Configuration);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        row["Visibility"].Should().Be("default");
        Detail(row, "conditionKey").Should().Be("expected_run_missing");
        DetailNames(row).Should().BeEquivalentTo(arranged.ExpectedDetailNames, "the details hold the declared fields and nothing else");
        if (arranged.ExpectedChanged is not null)
            DetailList(row, "changedFields").Should().Equal(arranged.ExpectedChanged);

        if (arranged.IntegrationId is null)
            row["ResourceContextId"].Should().BeNull("the instance default belongs to no resource context");
        else
        {
            row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue, "the integration's owning context");
            Detail(row, "name").Should().Be("Moving integration", "the integration's name at the time");
        }

        RowText(rows).Should().NotContain(PrimarySentinel).And.NotContain(EscalationSentinel)
            .And.NotContain(SentinelSeconds.ToString()).And.NotContain(TimeSpan.FromSeconds(SentinelSeconds).Ticks.ToString());
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_stored_row_holds_what_was_requested_and_nothing_else_moved(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        await SeedTeamsAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var other = await SeedIntegrationAsync(scratch);
        await SeedOverrideAsync(scratch, other);
        var otherBefore = RowText(await OverrideRowsAsync(scratch, other));

        await using (var db = scratch.NewDb())
        {
            await Routing(db, NewUnit(db, principal)).UpsertIntegrationRoutingAsync(
                new IntegrationId(integrationId), Key,
                Command(enabled: false, sendResolved: false, suppressReminders: true, suppressEscalation: true,
                    escalationAfter: TimeSpan.FromSeconds(SentinelSeconds), reminderInterval: TimeSpan.FromSeconds(7), primaryTeam: "  " + PrimarySentinel + " ", escalationTeam: ""));
        }

        var row = (await OverrideRowsAsync(scratch, integrationId)).Should().ContainSingle().Subject;
        row["IsEnabled"].Should().Be(false);
        row["SendResolvedNotification"].Should().Be(false);
        row["SuppressRemindersWhenAcknowledged"].Should().Be(true);
        row["SuppressEscalationWhenAcknowledged"].Should().Be(true);
        row["EscalationAfterTicks"].Should().Be(TimeSpan.FromSeconds(SentinelSeconds).Ticks);
        row["ReminderIntervalTicks"].Should().Be(TimeSpan.FromSeconds(7).Ticks);
        row["PrimaryOperationalTeamId"].Should().Be(PrimarySentinel, "the id is stored trimmed");
        row["EscalationOperationalTeamId"].Should().BeNull("a blank id is none");
        RowText(await OverrideRowsAsync(scratch, other)).Should().Be(otherBefore, "another integration's override is untouched");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_condition_the_platform_does_not_define_is_recorded_as_other_and_its_text_reaches_no_row(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        const string custom = "customer supplied condition text";
        var lastAudit = await LastAuditIdAsync(scratch);

        await using (var db = scratch.NewDb())
            await Routing(db, NewUnit(db, principal)).UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), custom, Command());
        await using (var db = scratch.NewDb())
            await Routing(db, NewUnit(db, principal)).UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), custom, Command(enabled: false));
        await using (var db = scratch.NewDb())
            (await Routing(db, NewUnit(db, principal)).ClearIntegrationRoutingAsync(new IntegrationId(integrationId), custom)).Should().BeTrue();

        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        rows.Select(r => (string)r["Action"]!).Should().Equal(
            AuditActionNames.AttentionRoutingOverrideCreated, AuditActionNames.AttentionRoutingOverrideUpdated, AuditActionNames.AttentionRoutingOverrideCleared);
        rows.Should().OnlyContain(r => Detail(r, "conditionKey") == "other");
        RowText(rows).Should().NotContain(custom);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- atomic, and a request that changes nothing writes nothing ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_request_that_changes_nothing_writes_nothing_and_leaves_every_row_as_it_was(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        await ResetDefaultAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        await SeedOverrideAsync(scratch, integrationId);
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using (var db = scratch.NewDb())
            await Routing(db, NewUnit(db, principal)).UpsertInstanceDefaultRoutingAsync(Key, DefaultBaseline() with { PrimaryOperationalTeamId = "   " });
        await using (var db = scratch.NewDb())
            await Routing(db, NewUnit(db, principal)).UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), Key, Baseline());
        await using (var db = scratch.NewDb())
            (await Routing(db, NewUnit(db, principal)).ClearIntegrationRoutingAsync(new IntegrationId(integrationId), "no_such_condition")).Should().BeFalse();

        (await SnapshotAsync(scratch)).Should().Be(before, "not a column of any routing row moved");
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().BeEmpty("nothing changed, so nothing is recorded");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_refusal_inside_the_unit_leaves_no_row_and_no_audit_row(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        await ResetDefaultAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using (var db = scratch.NewDb())
        {
            var routing = Routing(db, NewUnit(db, principal));
            (await FluentActions.Awaiting(() => routing.UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), Key, Command(primaryTeam: "team-gone")).AsTask())
                .Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<AuditInvariantException>();
            await FluentActions.Awaiting(() => routing.UpsertInstanceDefaultRoutingAsync(Key, DefaultBaseline() with { EscalationOperationalTeamId = "team-gone" }).AsTask())
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => routing.UpsertInstanceDefaultRoutingAsync("no_such_condition", DefaultBaseline() with { IsEnabled = false }).AsTask())
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => routing.UpsertIntegrationRoutingAsync(new IntegrationId("int-does-not-exist"), Key, Command()).AsTask())
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => routing.ClearIntegrationRoutingAsync(new IntegrationId("int-does-not-exist"), Key).AsTask())
                .Should().ThrowAsync<InvalidOperationException>();
        }

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// Two default updates queued on the instance routing row while a third session holds it. The second compares against the row
    /// the first left, so it names only what the first did not already set, and the second's rows record the order they ran in.
    /// </summary>
    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task Two_default_updates_queued_on_the_row_each_compare_against_what_the_other_left(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        await ResetDefaultAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "InstanceAttentionRoutings", InstanceAttentionRoutingDefaults.RowId);
        Task<Outcome<bool>> Update(AttentionRoutingUpsertCommand command) => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            await Routing(db, NewUnit(db, principal)).UpsertInstanceDefaultRoutingAsync(Key, command);
            return true;
        });

        var first = Update(DefaultBaseline() with { IsEnabled = false });
        await holder.WaitUntilBlockedAsync(scratch, "InstanceAttentionRoutings", first);
        var second = Update(DefaultBaseline() with { IsEnabled = false, ReminderInterval = TimeSpan.FromMinutes(3) });
        await holder.WaitUntilBlockedAsync(scratch, "InstanceAttentionRoutings", Task.WhenAny(first, second), minimumWaiters: 2);
        await holder.CommitAsync();

        (await first).Error.Should().BeNull();
        (await second).Error.Should().BeNull();
        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        rows.Should().HaveCount(2);
        DetailList(rows[0], "changedFields").Should().Equal("enabled");
        DetailList(rows[1], "changedFields").Should().Equal("reminderInterval");
        var row = (await DefaultRowsAsync(scratch)).Single();
        row["IsEnabled"].Should().Be(false);
        row["ReminderIntervalTicks"].Should().Be(TimeSpan.FromMinutes(3).Ticks);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- authorization after an ownership move ----

    /// <summary>
    /// A write waits for the integration row while a third session moves the integration to another context and commits. The
    /// re-run authorization runs under the lock and sees the integration in its new context. A caller who still may update it
    /// writes, and the row names the new owner. A caller who lost <c>integration.update</c> through the move is refused, and the
    /// routing row and the audit table are exactly as they were.
    /// </summary>
    [Theory]
    [MemberData(nameof(MoveCases))]
    public async Task An_ownership_move_that_commits_while_the_write_waits_is_seen_by_the_recheck(Op op, bool loseAccess, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var targetContext = await OwnershipSqlSeed.SeedContextAsync(scratch, "Routing move target");
        if (op != Op.Create)
            await SeedOverrideAsync(scratch, integrationId);
        var observed = new List<string>();
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        var write = RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            var routing = Routing(db, NewUnit(db, principal));
            Func<CancellationToken, ValueTask> recheck = async _ =>
            {
                // The integration as it is when the check runs: after the lock is held, so after the move committed.
                var owner = await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integrationId}';");
                observed.Add(owner);
                if (loseAccess && owner == targetContext)
                    throw new UnauthorizedAccessException("The caller no longer holds integration.update on the integration.");
            };

            if (op == Op.Clear)
                await routing.ClearIntegrationRoutingAsync(new IntegrationId(integrationId), Key, reauthorize: recheck);
            else
                await routing.UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), Key, Command(enabled: false), reauthorize: recheck);
            return true;
        });
        await holder.WaitUntilBlockedAsync(scratch, "Integrations", write);

        // An ownership move commits while the write waits for the integration row.
        await holder.ExecuteAsync($"UPDATE Integrations SET ResourceContextId = N'{targetContext}' WHERE Id = N'{integrationId}';");
        await holder.CommitAsync();
        var outcome = await write;

        observed.Should().ContainSingle("the check ran once, under the lock, against the integration as moved")
            .Which.Should().Be(targetContext);
        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        if (loseAccess)
        {
            outcome.Error.Should().BeOfType<UnauthorizedAccessException>();
            (await SnapshotAsync(scratch)).Should().Be(before, "a refused write changes nothing");
            rows.Should().BeEmpty("a refused write records nothing");
        }
        else
        {
            outcome.Error.Should().BeNull();
            (await SnapshotAsync(scratch)).Should().NotBe(before);
            var row = rows.Should().ContainSingle().Subject;
            row["ResourceContextId"].Should().Be(targetContext, "the row names the owner the unit locked, not the one the caller first read");
        }

        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- an integration delete and an override write queued on the integration row ----

    private static async Task<Exception?> DeleteIntegrationAsync(AuditSqlScratch scratch, string integrationId, IPrincipalContext principal)
    {
        try
        {
            await using var db = scratch.NewDb();
            var store = new SqlSecretStore(db, new ConnectionSqlHarness.PassThroughProtector(), new DefaultSecretAuditActorProvider());
            var service = new IntegrationManagementService(
                principal,
                new ConnectionSqlHarness.AllowAll(),
                new SqlIntegrationRepository(db),
                new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
                db,
                new SqlResourceOwnershipMutationGate(db),
                new IntegrationSettingSecretClassifier(new IntegrationSettingSecretInventory(db), store, new EmptySettingRegistry()),
                new IntegrationSettingSecretRetirement(store),
                NewUnit(db, principal));
            await service.DeleteIntegrationAsync(new IntegrationId(integrationId));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class EmptySettingRegistry : IIntegrationSettingDefinitionRegistry
    {
        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [];

        public PlatformSettingDefinition? TryGet(string key) => null;
    }

    /// <summary>
    /// A delete and an override create are both queued behind a held integration row, in both arrival orders. The delete first
    /// leaves no integration, so the create is refused as not found and leaves no override and no audit row. The create first
    /// commits the override and its row, and the delete then counts it as a blocker and is refused: the integration, the override
    /// and the audit row remain, and no delete row exists.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeleteRaces))]
    public async Task A_delete_and_an_override_create_queued_on_the_integration_row_never_leave_an_orphan(bool deleteFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        Task<Exception?> Delete() => Task.Run(() => DeleteIntegrationAsync(scratch, integrationId, principal));
        Task<Outcome<bool>> Create() => RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            await Routing(db, NewUnit(db, principal)).UpsertIntegrationRoutingAsync(new IntegrationId(integrationId), Key, Command());
            return true;
        });

        Task<Exception?> deleteTask;
        Task<Outcome<bool>> createTask;
        if (deleteFirst)
        {
            deleteTask = Delete();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", deleteTask);
            createTask = Create();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(deleteTask, createTask), minimumWaiters: 2);
        }
        else
        {
            createTask = Create();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", createTask);
            deleteTask = Delete();
            await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(deleteTask, createTask), minimumWaiters: 2);
        }

        await holder.CommitAsync();
        var deleted = await deleteTask;
        var created = await createTask;

        var integrationCount = await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM Integrations WHERE Id = N'{integrationId}';");
        var overrides = await OverrideRowsAsync(scratch, integrationId);
        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        if (deleteFirst)
        {
            deleted.Should().BeNull("nothing blocked the delete when it held the row");
            created.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("was not found");
            integrationCount.Should().Be(0);
            overrides.Should().BeEmpty("no override was added to a deleted integration");
            rows.Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.IntegrationDeleted);
        }
        else
        {
            created.Error.Should().BeNull();
            deleted.Should().BeOfType<IntegrationDeleteBlockedException>().Which.Blocker.Should().Be(IntegrationDeleteBlocker.AttentionRoutings);
            integrationCount.Should().Be(1);
            overrides.Should().ContainSingle("the committed override is what blocked the delete");
            rows.Select(r => (string)r["Action"]!).Should().Equal(AuditActionNames.AttentionRoutingOverrideCreated);
            rows.Single()["TargetId"].Should().Be(integrationId);
        }

        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    private async Task<(AuditedOperationM6Runner Runner, IPrincipalContext Principal)> RunnerAsync(AuditSqlScratch scratch, Op op)
    {
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, op);
        var runner = new AuditedOperationM6Runner(scratch, () => SnapshotAsync(scratch), arranged.Action, arranged.Run);
        return (runner, principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_audit_insert_failure_leaves_the_routing_exactly_as_it_was(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (runner, principal) = await RunnerAsync(scratch, op);
        await runner.AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (runner, principal) = await RunnerAsync(scratch, op);
        await runner.LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Fact]
    public async Task A_clear_whose_acknowledgement_was_lost_reports_success_with_the_override_gone_and_one_row()
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        await SeedOverrideAsync(scratch, integrationId);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        bool cleared;
        await using (var db = scratch.NewDb(lost, attempts))
            cleared = await Routing(db, NewUnit(db, principal)).ClearIntegrationRoutingAsync(new IntegrationId(integrationId), Key);

        cleared.Should().BeTrue("a verified commit is the removal having happened, whatever the table now holds");
        attempts.Started.Should().Be(1, "a verified commit is not run again");
        (await OverrideRowsAsync(scratch, integrationId)).Should().BeEmpty();
        (await AuditRowsAfterAsync(scratch, 0)).Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.AttentionRoutingOverrideCleared);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task A_confirmed_rollback_reruns_the_operation_once_and_ends_with_one_change_and_one_row(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (runner, principal) = await RunnerAsync(scratch, op);
        await runner.ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Op op, bool committed)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (runner, principal) = await RunnerAsync(scratch, op);
        await runner.UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [MemberData(nameof(Ops))]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(Op op)
    {
        await using var scratch = await NewScratchAsync(rcsi: false);
        var (runner, principal) = await RunnerAsync(scratch, op);
        await runner.OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }
}
