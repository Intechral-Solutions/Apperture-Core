using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Core.Domain.Scheduling;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.ConnectionSqlHarness;
using static Apperture.IntegrationTests.Audit.OwnershipSqlSeed;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// AUD-1.4 slice 5: the integration, connection and schedule ownership moves against SQL Server, on a locking READ COMMITTED
/// catalog and on a READ_COMMITTED_SNAPSHOT catalog (design gate §13.2, §13.5, M6, M7). Each move is one audited unit: the
/// ownership change, every dependent update (an integration's attached schedules) and the <c>moved_out</c> and <c>moved_in</c> pair
/// commit together or not at all; a blocked move changes nothing and is recorded after the unit as a rejection. This replaces the
/// earlier suite that ran the move over the legacy audit bridge. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class OwnershipMoveSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public OwnershipMoveSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    public enum Kind
    {
        Integration,
        IntegrationWithSchedules,
        Connection,
        Schedule,
    }

    public static IEnumerable<object[]> Cases() =>
        from kind in Enum.GetValues<Kind>()
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    public static IEnumerable<object[]> Kinds() => new[] { Kind.IntegrationWithSchedules, Kind.Connection, Kind.Schedule }.Select(k => new object[] { k });

    public static IEnumerable<object[]> OutcomeCases() =>
        from kind in new[] { Kind.IntegrationWithSchedules, Kind.Connection }
        from committed in new[] { true, false }
        select new object[] { kind, committed };

    // ---- M7: the committed move ----

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_committed_move_records_the_pair_each_related_to_its_own_context_with_one_correlation(Kind kind, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, kind, principal);
        var before = await SnapshotAsync(scratch);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        (await SnapshotAsync(scratch)).Should().NotBe(before);
        var rows = await AuditRowsAsync(scratch, arranged.ResourceId);
        rows.Select(r => (string)r["Action"]!).Should().Equal(arranged.MovedOut, arranged.MovedIn);
        var movedOut = rows[0];
        var movedIn = rows[1];
        foreach (var row in rows)
        {
            row["Actor"].Should().Be(admin);
            row["Category"].Should().Be(AuditCategories.Governance);
            row["Severity"].Should().Be(AuditSeverities.Standard);
            row["Basis"].Should().Be(AuditBases.Change);
            row["TargetType"].Should().Be(arranged.TargetType);
            row["TargetId"].Should().Be(arranged.ResourceId);
            row["Outcome"].Should().Be(AuditOutcomeCodes.Succeeded);
            row["OperationEventId"].Should().Be(movedOut["EventId"], "both rows belong to the moved_out row's operation");
            row["CorrelationId"].Should().Be(movedOut["EventId"], "the pair shares the operation's event id as its correlation");
            Detail(row, "sourceContextId").Should().Be(arranged.Source);
            Detail(row, "targetContextId").Should().Be(arranged.Target);
        }

        movedOut["OperationEventId"].Should().Be(movedOut["EventId"], "moved_out is the primary row");
        movedOut["ResourceContextId"].Should().Be(arranged.Source, "a filter by the source context finds what left it");
        movedIn["ResourceContextId"].Should().Be(arranged.Target, "a filter by the target context finds what arrived in it");
        arranged.AssertMoved(scratch, rows);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filtering_by_the_source_sees_moved_out_by_the_target_moved_in_and_unfiltered_sees_both(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, Kind.Connection, principal);
        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        var atSource = await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE ResourceContextId = N'{arranged.Source}' AND TargetId = N'{arranged.ResourceId}';");
        var atTarget = await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE ResourceContextId = N'{arranged.Target}' AND TargetId = N'{arranged.ResourceId}';");
        var global = await AuditRowsAsync(scratch, arranged.ResourceId);

        atSource.Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.ConnectionMovedOut);
        atTarget.Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.ConnectionMovedIn);
        global.Should().HaveCount(2);
    }

    // ---- A move with attached schedules ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_integration_move_writes_exactly_two_rows_however_many_schedules_move_and_lists_them(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, Kind.IntegrationWithSchedules, principal);

        await using (var db = scratch.NewDb())
            await arranged.Run(db, NewUnit(db, principal));

        var rows = await AuditRowsAsync(scratch, arranged.ResourceId);
        rows.Should().HaveCount(2, "attached schedules move with the integration and never get rows of their own");
        Detail(rows[0], "schedulesMoved").Should().Be("2");
        using var details = JsonDocument.Parse((string)rows[0]["Details"]!);
        details.RootElement.GetProperty("scheduleIds").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(arranged.ScheduleIds);
        foreach (var schedule in arranged.ScheduleIds!)
        {
            (await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM ScheduleDefinitions WHERE Id = N'{schedule}';")).Should().Be(arranged.Target);
            (await AuditRowsAsync(scratch, schedule)).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failure_after_the_integration_row_moved_rolls_back_the_integration_and_its_schedules_together(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, Kind.IntegrationWithSchedules, principal);
        var before = await SnapshotAsync(scratch);
        var fail = new FailingCommand(sql => sql.Contains("UPDATE", StringComparison.Ordinal) && sql.Contains("[ScheduleDefinitions]", StringComparison.Ordinal));

        await using (var db = scratch.NewDb(fail))
        {
            var act = () => arranged.Run(db, NewUnit(db, principal));
            await act.Should().ThrowAsync<Exception>();
        }

        fail.Failures.Should().BeGreaterThan(0, "the failure was really injected, after the integration's own update ran");
        (await SnapshotAsync(scratch)).Should().Be(before, "neither the integration nor either schedule moved");
        (await AuditRowsAsync(scratch, arranged.ResourceId)).Should().BeEmpty();
    }

    // ---- A blocked move ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_blocked_move_changes_nothing_and_is_recorded_after_the_unit_as_a_rejection_at_the_source(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Blocked source");
        var target = await SeedContextAsync(scratch, "Blocked target");
        var thirdParty = await SeedContextAsync(scratch, "Third party");
        var integration = await SeedIntegrationAsync(scratch, source);
        var connection = await SeedConnectionAsync(scratch, thirdParty);
        await using (var seedDb = scratch.NewDb())
        {
            await new SqlBindingRepository(seedDb).AddAsync(new Binding(
                new BindingId($"b-{Guid.NewGuid():N}"), new IntegrationId(integration), "Sample.Mod", "slot-a", "test.contract",
                new ConnectionId(connection), null, BindingState.Active));
        }

        var before = await SnapshotAsync(scratch);
        ResourceMovePreview preview;
        await using (var db = scratch.NewDb())
            preview = await MoveService(scratch, db, principal, NewUnit(db, principal)).MoveIntegrationAsync(new IntegrationId(integration), new ResourceContextId(target));

        preview.CanMove.Should().BeFalse();
        (await SnapshotAsync(scratch)).Should().Be(before, "a blocked move mutates nothing");
        var row = (await AuditRowsAsync(scratch, integration)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.IntegrationMoveRejected);
        row["Actor"].Should().Be(admin);
        row["Basis"].Should().Be(AuditBases.Attempt);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Rejected);
        row["ResourceContextId"].Should().Be(source, "a rejection is the source's event; the target never heard about it");
        using var details = JsonDocument.Parse((string)row["Details"]!);
        details.RootElement.GetProperty("blockers").EnumerateArray().Select(e => e.GetString()).Should().Contain("binding_connection_unreachable");
        (await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE ResourceContextId = N'{target}';")).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    public enum ScheduleBlock
    {
        AttachedToIntegration,
        ArchivedTarget,
    }

    public static IEnumerable<object[]> ScheduleBlocks() =>
        from block in Enum.GetValues<ScheduleBlock>()
        from rcsi in new[] { false, true }
        select new object[] { block, rcsi };

    [Theory]
    [MemberData(nameof(ScheduleBlocks))]
    public async Task A_blocked_schedule_move_changes_nothing_and_is_recorded_after_the_unit_as_a_rejection_at_the_source(ScheduleBlock block, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Blocked schedule source");
        var target = await SeedContextAsync(
            scratch, "Blocked schedule target", block == ScheduleBlock.ArchivedTarget ? ResourceContextStates.Archived : ResourceContextStates.Active);
        var integration = block == ScheduleBlock.AttachedToIntegration ? await SeedIntegrationAsync(scratch, source) : null;
        var schedule = await SeedScheduleAsync(scratch, source, integration);
        var expectedBlocker = block == ScheduleBlock.AttachedToIntegration
            ? ResourceMoveBlockerCodes.ScheduleAttachedToIntegration
            : ResourceMoveBlockerCodes.TargetArchived;
        var before = await SnapshotAsync(scratch);

        ResourceMovePreview preview;
        await using (var db = scratch.NewDb())
            preview = await MoveService(scratch, db, principal, NewUnit(db, principal)).MoveScheduleAsync(new ScheduleId(schedule), new ResourceContextId(target));

        preview.CanMove.Should().BeFalse();
        preview.Blockers.Select(b => b.Code).Should().Equal(expectedBlocker);
        (await SnapshotAsync(scratch)).Should().Be(before, "a blocked move mutates nothing");
        (await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM ScheduleDefinitions WHERE Id = N'{schedule}';")).Should().Be(source);

        // Exactly the one rejection row for this schedule: no moved_out or moved_in, and no row of any kind at the target.
        var row = (await AuditRowsAsync(scratch, schedule)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ScheduleMoveRejected);
        row["Actor"].Should().Be(admin);
        row["Category"].Should().Be(AuditCategories.Governance);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Attempt);
        row["Outcome"].Should().Be(AuditOutcomeCodes.Rejected);
        row["Visibility"].Should().Be("default");
        row["TargetType"].Should().Be(AuditTargetTypes.Schedule);
        row["TargetId"].Should().Be(schedule);
        row["ResourceContextId"].Should().Be(source, "a rejection is the source's event; the target never heard about it");
        Detail(row, "name").Should().Be("Moving schedule");
        Detail(row, "sourceContextId").Should().Be(source);
        Detail(row, "targetContextId").Should().Be(target);
        using var details = JsonDocument.Parse((string)row["Details"]!);
        details.RootElement.GetProperty("blockers").EnumerateArray().Select(e => e.GetString()).Should().Equal(expectedBlocker);
        (await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE ResourceContextId = N'{target}';")).Should().BeEmpty();
        (await scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE Action IN (N'{AuditActionNames.ScheduleMovedOut}', N'{AuditActionNames.ScheduleMovedIn}') AND TargetId = N'{schedule}';"))
            .Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rejection_that_cannot_be_written_never_changes_the_answer(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Quiet source");
        var integration = await SeedIntegrationAsync(scratch, source);
        var fail = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));
        ResourceMovePreview preview;

        await using (var db = scratch.NewDb())
        {
            // Moving into the context it is already in is a blocked move.
            var service = new ResourceOwnershipMoveService(
                principal, new AllowAll(), new SqlIntegrationRepository(db), new SqlConnectionRepository(db), new SqlScheduleRepository(db),
                new SqlBindingRepository(db), new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
                new SqlResourceOwnershipMutationGate(db), NewUnit(db, principal),
                new SqlAuditEventWriter(scratch.Options(fail), principals: principal));
            preview = await service.MoveIntegrationAsync(new IntegrationId(integration), new ResourceContextId(source));
        }

        preview.CanMove.Should().BeFalse();
        fail.Failures.Should().BeGreaterThan(0);
        (await AuditRowsAsync(scratch, integration)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_move_into_an_archived_context_is_blocked_and_recorded_with_its_reason(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var source = await SeedContextAsync(scratch, "Move source");
        var target = await SeedContextAsync(scratch, "Archived target", ResourceContextStates.Archived);
        var connection = await SeedConnectionAsync(scratch, source);

        await using (var db = scratch.NewDb())
            (await MoveService(scratch, db, principal, NewUnit(db, principal)).MoveConnectionAsync(new ConnectionId(connection), new ResourceContextId(target)))
                .CanMove.Should().BeFalse();

        var row = (await AuditRowsAsync(scratch, connection)).Should().ContainSingle().Subject;
        row["Action"].Should().Be(AuditActionNames.ConnectionMoveRejected);
        Detail(row, "targetContextId").Should().Be(target);
        (await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Connections WHERE Id = N'{connection}';")).Should().Be(source);
    }

    // ---- M6 ----

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_audit_insert_failure_leaves_no_partial_move(Kind kind)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Mv", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, kind, principal)).AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(Kind kind)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Mv", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, kind, principal)).LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_confirmed_rollback_reruns_the_move_once_with_one_pair(Kind kind)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Mv", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, kind, principal)).ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(Kind kind, bool committed)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Mv", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, kind, principal)).UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Fact]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_moves_nothing()
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Aud14Mv", rcsi: false);
        var (_, principal) = await AdminAsync(scratch);
        await (await RunnerAsync(scratch, Kind.IntegrationWithSchedules, principal)).OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lost_acknowledgement_of_a_move_returns_a_committed_result_built_from_the_pair(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var arranged = await ArrangeAsync(scratch, Kind.IntegrationWithSchedules, principal);
        ResourceMovePreview preview;

        await using (var db = scratch.NewDb(new LostCommitAcknowledgement()))
        {
            var service = MoveService(scratch, db, principal, NewUnit(db, principal));
            preview = await service.MoveIntegrationAsync(new IntegrationId(arranged.ResourceId), new ResourceContextId(arranged.Target));
        }

        preview.CanMove.Should().BeTrue();
        preview.Source.Id.Should().Be(arranged.Source);
        preview.Target.Id.Should().Be(arranged.Target);
        preview.Effects.Should().ContainSingle(e => e.Code == ResourceMoveEffectCodes.SchedulesMoveWithIntegration)
            .Which.RelatedResourceIds.Should().BeEquivalentTo(arranged.ScheduleIds);
        (await AuditRowsAsync(scratch, arranged.ResourceId)).Should().HaveCount(2);
    }

    // ---- wiring ----

    private static ResourceOwnershipMoveService MoveService(AuditSqlScratch scratch, AppertureDbContext db, IPrincipalContext principal, IAuditedUnitOfWork unit) =>
        new(
            principal,
            new AllowAll(),
            new SqlIntegrationRepository(db),
            new SqlConnectionRepository(db),
            new SqlScheduleRepository(db),
            new SqlBindingRepository(db),
            new ResourceContextService(new SqlResourceContextRepository(db), TimeProvider.System),
            new SqlResourceOwnershipMutationGate(db),
            unit,
            new SqlAuditEventWriter(scratch.Options(), principals: principal));

    private sealed record Arranged(
        string TargetType,
        string ResourceId,
        string Source,
        string Target,
        string MovedOut,
        string MovedIn,
        Func<AppertureDbContext, IAuditedUnitOfWork, Task> Run,
        Action<AuditSqlScratch, List<Dictionary<string, object?>>> AssertMoved,
        IReadOnlyList<string>? ScheduleIds = null);

    private async Task<Arranged> ArrangeAsync(AuditSqlScratch scratch, Kind kind, IPrincipalContext principal)
    {
        var source = await SeedContextAsync(scratch, "Move source");
        var target = await SeedContextAsync(scratch, "Move target");
        switch (kind)
        {
            case Kind.Integration:
            case Kind.IntegrationWithSchedules:
            {
                var integration = await SeedIntegrationAsync(scratch, source);
                var schedules = kind == Kind.Integration
                    ? new List<string>()
                    : [await SeedScheduleAsync(scratch, source, integration), await SeedScheduleAsync(scratch, source, integration)];
                schedules.Sort(StringComparer.Ordinal);
                return new Arranged(
                    AuditTargetTypes.Integration, integration, source, target, AuditActionNames.IntegrationMovedOut, AuditActionNames.IntegrationMovedIn,
                    (db, unit) => MoveService(scratch, db, principal, unit).MoveIntegrationAsync(new IntegrationId(integration), new ResourceContextId(target)).AsTask(),
                    (s, _) =>
                    {
                        s.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integration}';").Result.Should().Be(target);
                    },
                    schedules);
            }

            case Kind.Connection:
            {
                var connection = await SeedConnectionAsync(scratch, source);
                return new Arranged(
                    AuditTargetTypes.Connection, connection, source, target, AuditActionNames.ConnectionMovedOut, AuditActionNames.ConnectionMovedIn,
                    (db, unit) => MoveService(scratch, db, principal, unit).MoveConnectionAsync(new ConnectionId(connection), new ResourceContextId(target)).AsTask(),
                    (s, rows) =>
                    {
                        s.ScalarAsync<string>($"SELECT ResourceContextId FROM Connections WHERE Id = N'{connection}';").Result.Should().Be(target);
                        rows.Should().OnlyContain(r => Detail(r, "schedulesMoved") == null, "only an integration move counts schedules");
                    });
            }

            default:
            {
                var schedule = await SeedScheduleAsync(scratch, source, integration: null);
                return new Arranged(
                    AuditTargetTypes.Schedule, schedule, source, target, AuditActionNames.ScheduleMovedOut, AuditActionNames.ScheduleMovedIn,
                    (db, unit) => MoveService(scratch, db, principal, unit).MoveScheduleAsync(new ScheduleId(schedule), new ResourceContextId(target)).AsTask(),
                    (s, _) =>
                    {
                        s.ScalarAsync<string>($"SELECT ResourceContextId FROM ScheduleDefinitions WHERE Id = N'{schedule}';").Result.Should().Be(target);
                    });
            }
        }
    }

    private async Task<AuditedOperationM6Runner> RunnerAsync(AuditSqlScratch scratch, Kind kind, IPrincipalContext principal)
    {
        var arranged = await ArrangeAsync(scratch, kind, principal);
        return new AuditedOperationM6Runner(scratch, () => SnapshotAsync(scratch), arranged.MovedOut, arranged.Run);
    }

    /// <summary>Every row a move can change, as one comparable text.</summary>
    private static async Task<string> SnapshotAsync(AuditSqlScratch scratch)
    {
        static string Text(IEnumerable<Dictionary<string, object?>> rows) =>
            string.Join("|", rows.Select(r => string.Join(",", r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString()))));

        return Text(await scratch.RowsAsync("SELECT * FROM Integrations ORDER BY Id;"))
            + "#" + Text(await scratch.RowsAsync("SELECT * FROM Connections ORDER BY Id;"))
            + "#" + Text(await scratch.RowsAsync("SELECT * FROM ScheduleDefinitions ORDER BY Id;"));
    }
}
