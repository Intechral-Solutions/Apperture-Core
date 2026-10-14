using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.ResourceContexts;
using Apperture.Core.Application.Scheduling;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Integration;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// SEC-2.1: the acknowledgement of an attention incident as an audited unit against SQL Server, on locking READ COMMITTED and
/// READ_COMMITTED_SNAPSHOT catalogs.
/// <list type="bullet">
/// <item>The committed operation: the three columns and exactly one <c>attention_incident.acknowledged</c> row, attributed to the
/// operator the unit resolved, naming the integration, the incident, the condition, when it started and whether a note was given,
/// and never the note.</item>
/// <item>Repeating changes nothing and records nothing; of two acknowledgements queued on the integration row one wins and the
/// other is the no-op, in both arrival orders, and the winner is never overwritten.</item>
/// <item>The caller's authorization is run again once the integration row is locked: an ownership move that commits while the
/// acknowledgement waits is seen by it, and a caller who lost <c>integration.update</c> writes nothing.</item>
/// <item>An acknowledgement and the observation worker's resolution queued on the incident row end either acknowledged then
/// resolved (the acknowledgement columns survive the resolution) or as a refusal; the columns are never lost.</item>
/// <item>M6: an audit insert failure leaves nothing, a lost acknowledgement is verified and answers as the acknowledgement it was,
/// a confirmed rollback reruns once, an unknown outcome is wholly committed or wholly absent, an operation lock that is not
/// granted changes nothing.</item>
/// </list>
/// Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AttentionIncidentAcknowledgementSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Key = AttentionConditionKeys.ExpectedRunMissing;

    /// <summary>A note that must never reach an audit row, a log line or an exception message.</summary>
    private const string NoteSentinel = "note-sentinel-do-not-record";

    private static readonly DateTimeOffset Started = DateTimeOffset.Parse("2025-01-10T12:00:00Z");

    private readonly AuditIsolationCatalogs _catalogs;
    private readonly ITestOutputHelper _output;

    public AttentionIncidentAcknowledgementSqlServerTests(AuditIsolationCatalogs catalogs, ITestOutputHelper output)
    {
        _catalogs = catalogs;
        _output = output;
    }

    /// <summary>A catalog of this case's own, for the cases that need an empty audit table (the M6 matrix). The audit table is append-only.</summary>
    private static Task<AuditSqlScratch> NewScratchAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Sec21Ack", rcsi);

    public static IEnumerable<object[]> Isolation() => [[false], [true]];

    public static IEnumerable<object[]> OutcomeCases() =>
        from committed in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { committed, rcsi };

    public static IEnumerable<object[]> ArrivalOrders() =>
        from firstIsA in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { firstIsA, rcsi };

    public static IEnumerable<object[]> MoveCases() =>
        from loseAccess in new[] { false, true }
        from rcsi in new[] { false, true }
        select new object[] { loseAccess, rcsi };

    // ---- the service over a real catalog ----

    private static AttentionIncidentAcknowledgementService Service(AppertureDbContext db, IAuditedUnitOfWork unit) =>
        new(db, new SqlIntegrationRepository(db), unit, TimeProvider.System);

    private static ValueTask Permitted(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private static Task<AttentionIncidentAcknowledgementOutcome> AckAsync(
        AuditSqlScratch scratch,
        IPrincipalContext principal,
        string integrationId,
        string incidentId,
        string? note = null,
        Func<CancellationToken, ValueTask>? reauthorize = null)
    {
        return Run();

        async Task<AttentionIncidentAcknowledgementOutcome> Run()
        {
            await using var db = scratch.NewDb();
            return await Service(db, NewUnit(db, principal)).AcknowledgeAsync(
                new IntegrationId(integrationId), incidentId, note, reauthorize ?? Permitted);
        }
    }

    // ---- arranging ----

    private static Task<string> SeedIntegrationAsync(AuditSqlScratch scratch) =>
        OwnershipSqlSeed.SeedIntegrationAsync(scratch, ResourceContextIds.SharedValue);

    /// <summary>An incident straight into the table (the audit table is append-only, so a fixture writes no audit row).</summary>
    private static async Task<string> SeedIncidentAsync(
        AuditSqlScratch scratch,
        string integrationId,
        bool resolved = false,
        string? acknowledgedBy = null,
        string? note = null)
    {
        var id = "attn_" + Guid.NewGuid().ToString("N");
        var status = resolved ? AttentionIncidentStatuses.Resolved : AttentionIncidentStatuses.Active;
        var resolvedAt = resolved ? "'2025-01-10T13:00:00+00:00'" : "NULL";
        var reason = resolved ? $"N'{AttentionIncidentResolutionReasons.ExpectationCleared}'" : "NULL";
        var ackAt = acknowledgedBy is null ? "NULL" : "'2025-01-10T12:30:00+00:00'";
        var ackBy = acknowledgedBy is null ? "NULL" : $"N'{acknowledgedBy}'";
        var ackNote = note is null ? "NULL" : $"N'{note}'";
        await scratch.ExecuteAsync(
            $"""
            INSERT INTO AttentionIncidents
              (Id, IntegrationId, ResourceContextId, ConditionKey, Status, StartedAtUtc, LastObservedAtUtc, ResolvedAtUtc, ResolutionReason,
               FirstExpectedFireAtUtc, LastExpectedFireAtUtc, AcknowledgedAtUtc, AcknowledgedBy, AcknowledgementNote)
            VALUES
              (N'{id}', N'{integrationId}', N'{ResourceContextIds.SharedValue}', N'{Key}', N'{status}', '2025-01-10T12:00:00+00:00',
               '2025-01-10T12:00:00+00:00', {resolvedAt}, {reason}, NULL, NULL, {ackAt}, {ackBy}, {ackNote});
            """);
        return id;
    }

    private static async Task<(string IntegrationId, string IncidentId)> ArrangeAsync(AuditSqlScratch scratch, bool resolved = false)
    {
        var integrationId = await SeedIntegrationAsync(scratch);
        return (integrationId, await SeedIncidentAsync(scratch, integrationId, resolved));
    }

    // ---- observation ----

    private static string RowText(IEnumerable<Dictionary<string, object?>> rows) =>
        JsonSerializer.Serialize(rows.Select(r => r.Values.Select(v => v is byte[] b ? Convert.ToBase64String(b) : v?.ToString())));

    /// <summary>Every incident row, as one comparable text.</summary>
    private static async Task<string> SnapshotAsync(AuditSqlScratch scratch) =>
        RowText(await scratch.RowsAsync("SELECT * FROM AttentionIncidents ORDER BY Id;"));

    private static async Task<Dictionary<string, object?>> IncidentAsync(AuditSqlScratch scratch, string incidentId) =>
        (await scratch.RowsAsync($"SELECT * FROM AttentionIncidents WHERE Id = N'{incidentId}';")).Single();

    private static Task<long> LastAuditIdAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<long>("SELECT ISNULL(MAX(Id), 0) FROM AuditLogs;");

    private static Task<List<Dictionary<string, object?>>> AuditRowsAfterAsync(AuditSqlScratch scratch, long afterId) =>
        scratch.RowsAsync($"SELECT * FROM AuditLogs WHERE Id > {afterId} ORDER BY Id;");

    private static string? Detail(Dictionary<string, object?> row, string name) => ConnectionSqlHarness.Detail(row, name);

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

    // ---- the committed operation ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_first_acknowledgement_commits_the_columns_and_one_row_attributed_to_the_operator_and_never_records_the_note(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        var outcome = await AckAsync(scratch, principal, integrationId, incidentId, "  " + NoteSentinel + "  ");

        outcome.Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);
        var incident = await IncidentAsync(scratch, incidentId);
        incident["AcknowledgedBy"].Should().Be(admin, "the actor is the unit's recorded actor");
        incident["AcknowledgementNote"].Should().Be(NoteSentinel, "the note is stored trimmed");
        incident["AcknowledgedAtUtc"].Should().NotBeNull();
        incident["Status"].Should().Be(AttentionIncidentStatuses.Active, "an acknowledgement does not resolve the incident");

        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        var row = rows.Should().ContainSingle("one primary row per completed operation").Subject;
        row["Action"].Should().Be(AuditActionNames.AttentionIncidentAcknowledged);
        row["OperationEventId"].Should().Be(row["EventId"], "the row is the primary row of its own operation");
        row["Actor"].Should().Be(admin);
        row["TargetType"].Should().Be(AuditTargetTypes.Integration);
        row["TargetId"].Should().Be(integrationId);
        row["OutcomeCode"].Should().Be(AuditOutcomeCodes.Succeeded);
        row["Category"].Should().Be(AuditCategories.Operations);
        row["Severity"].Should().Be(AuditSeverities.Standard);
        row["Basis"].Should().Be(AuditBases.Change);
        row["Visibility"].Should().Be("default");
        row["ResourceContextId"].Should().Be(ResourceContextIds.SharedValue, "the integration's owning context");
        DetailNames(row).Should().BeEquivalentTo(
            ["name", "incidentId", "conditionKey", "incidentStartedAt", "noteProvided"], "the details hold the declared fields and nothing else");
        Detail(row, "name").Should().Be("Moving integration");
        Detail(row, "incidentId").Should().Be(incidentId);
        Detail(row, "conditionKey").Should().Be("expected_run_missing");
        DateTimeOffset.Parse(Detail(row, "incidentStartedAt")!).Should().Be(Started);
        Detail(row, "noteProvided").Should().Be("true");
        RowText(rows).Should().NotContain(NoteSentinel);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_recorded_actor_is_the_authenticated_user_whatever_the_session_claims(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var userId = NewUserId();
        await scratch.AddUserAsync(userId, NewUsername("operator"));
        var principal = new Principal(userId, staleSessionName: "someone.else");
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await AckAsync(scratch, principal, integrationId, incidentId);

        (await IncidentAsync(scratch, incidentId))["AcknowledgedBy"].Should().Be(userId);
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().ContainSingle().Which["Actor"].Should().Be(userId);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_blank_note_is_stored_as_null_and_recorded_as_not_provided(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await AckAsync(scratch, principal, integrationId, incidentId, "   ");

        (await IncidentAsync(scratch, incidentId))["AcknowledgementNote"].Should().BeNull();
        Detail((await AuditRowsAfterAsync(scratch, lastAudit)).Single(), "noteProvided").Should().Be("false");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_note_of_exactly_the_column_width_is_stored_and_one_character_more_is_refused_before_the_database(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var (otherIntegration, otherIncident) = await ArrangeAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);
        var before = await SnapshotAsync(scratch);

        (await AckAsync(scratch, principal, otherIntegration, otherIncident, new string('n', AttentionIncidentAcknowledgementBounds.NoteMaxLength + 1)))
            .Should().Be(AttentionIncidentAcknowledgementOutcome.NoteTooLong);
        (await SnapshotAsync(scratch)).Should().Be(before, "a refused note changes nothing");
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().BeEmpty();

        var exact = new string('n', AttentionIncidentAcknowledgementBounds.NoteMaxLength);
        (await AckAsync(scratch, principal, integrationId, incidentId, exact)).Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);
        ((string)(await IncidentAsync(scratch, incidentId))["AcknowledgementNote"]!).Should().HaveLength(AttentionIncidentAcknowledgementBounds.NoteMaxLength);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- repeating ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_repeated_acknowledgement_changes_nothing_records_nothing_and_keeps_the_first_acknowledger_time_and_note(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (first, firstPrincipal) = await AdminAsync(scratch);
        var (_, secondPrincipal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        await AckAsync(scratch, firstPrincipal, integrationId, incidentId, "first note");
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        var outcome = await AckAsync(scratch, secondPrincipal, integrationId, incidentId, "second note");

        outcome.Should().Be(AttentionIncidentAcknowledgementOutcome.AlreadyAcknowledged);
        (await SnapshotAsync(scratch)).Should().Be(before, "not a column of any incident moved");
        var incident = await IncidentAsync(scratch, incidentId);
        incident["AcknowledgedBy"].Should().Be(first);
        incident["AcknowledgementNote"].Should().Be("first note");
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().BeEmpty("the repeat records nothing");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// Two acknowledgements by different operators queue on the integration row while a third session holds it, in both arrival
    /// orders. The lock manager grants in arrival order: the first commits its columns and its one row, the second then finds the
    /// incident acknowledged and is the no-op. The winner is never overwritten and exactly one row exists.
    /// </summary>
    [Theory]
    [MemberData(nameof(ArrivalOrders))]
    public async Task Two_concurrent_acknowledgements_give_one_winner_and_one_row(bool firstIsA, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (adminA, principalA) = await AdminAsync(scratch);
        var (adminB, principalB) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        Task<Outcome<AttentionIncidentAcknowledgementOutcome>> Ack(IPrincipalContext principal, string note) =>
            RunAsync(() => AckAsync(scratch, principal, integrationId, incidentId, note));

        var (firstPrincipal, firstNote, firstAdmin) = firstIsA ? (principalA, "note A", adminA) : (principalB, "note B", adminB);
        var (secondPrincipal, secondNote, _) = firstIsA ? (principalB, "note B", adminB) : (principalA, "note A", adminA);

        var first = Ack(firstPrincipal, firstNote);
        await holder.WaitUntilBlockedAsync(scratch, "Integrations", first);
        var second = Ack(secondPrincipal, secondNote);
        await holder.WaitUntilBlockedAsync(scratch, "Integrations", Task.WhenAny(first, second), minimumWaiters: 2);
        await holder.CommitAsync();

        var firstOutcome = await first;
        var secondOutcome = await second;
        firstOutcome.Error.Should().BeNull();
        secondOutcome.Error.Should().BeNull();
        firstOutcome.Value.Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);
        secondOutcome.Value.Should().Be(AttentionIncidentAcknowledgementOutcome.AlreadyAcknowledged);
        var incident = await IncidentAsync(scratch, incidentId);
        incident["AcknowledgedBy"].Should().Be(firstAdmin, "the winner is the first to arrive and is not overwritten");
        incident["AcknowledgementNote"].Should().Be(firstNote);
        var row = (await AuditRowsAfterAsync(scratch, lastAudit)).Should().ContainSingle("one winner, one row").Subject;
        row["Actor"].Should().Be(firstAdmin);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- states ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_resolved_incident_is_not_active_and_nothing_is_written_even_if_it_was_acknowledged_while_active(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var integrationId = await SeedIntegrationAsync(scratch);
        var resolved = await SeedIncidentAsync(scratch, integrationId, resolved: true);
        var acknowledgedThenResolved = await SeedIncidentAsync(scratch, await SeedIntegrationAsync(scratch), resolved: true, acknowledgedBy: "usr_first", note: "while active");
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        (await AckAsync(scratch, principal, integrationId, resolved, NoteSentinel)).Should().Be(AttentionIncidentAcknowledgementOutcome.NotActive);
        var ackedIntegration = (string)(await IncidentAsync(scratch, acknowledgedThenResolved))["IntegrationId"]!;
        (await AckAsync(scratch, principal, ackedIntegration, acknowledgedThenResolved, "again")).Should().Be(AttentionIncidentAcknowledgementOutcome.NotActive);

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_unknown_incident_another_integrations_incident_and_a_deleted_integration_are_all_not_found_and_change_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var other = await SeedIntegrationAsync(scratch);
        var orphanedIntegration = await SeedIntegrationAsync(scratch);
        var orphan = await SeedIncidentAsync(scratch, orphanedIntegration);
        await scratch.ExecuteAsync($"DELETE FROM Integrations WHERE Id = N'{orphanedIntegration}';");
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        (await AckAsync(scratch, principal, integrationId, "attn_missing")).Should().Be(AttentionIncidentAcknowledgementOutcome.NotFound);
        (await AckAsync(scratch, principal, other, incidentId)).Should().Be(AttentionIncidentAcknowledgementOutcome.NotFound);
        (await AckAsync(scratch, principal, orphanedIntegration, orphan)).Should().Be(
            AttentionIncidentAcknowledgementOutcome.NotFound, "the incident outlives its integration, but there is nothing to authorize against");

        (await SnapshotAsync(scratch)).Should().Be(before);
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().BeEmpty();
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- authorization ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_caller_who_may_not_update_changes_nothing_and_records_nothing(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        var act = () => AckAsync(
            scratch, principal, integrationId, incidentId, NoteSentinel, _ => throw new UnauthorizedAccessException("integration.update is required"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await SnapshotAsync(scratch)).Should().Be(before, "a refused acknowledgement changes no column");
        (await AuditRowsAfterAsync(scratch, lastAudit)).Should().BeEmpty("a refused acknowledgement records nothing");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    /// <summary>
    /// An acknowledgement waits for the integration row while a third session moves the integration to another context and
    /// commits. The re-run authorization runs under the lock and sees the integration in its new context. A caller who still may
    /// update it acknowledges, and the row names the new owner. A caller who lost <c>integration.update</c> through the move is
    /// refused, and the incident and the audit table are exactly as they were.
    /// </summary>
    [Theory]
    [MemberData(nameof(MoveCases))]
    public async Task An_ownership_move_that_commits_while_the_acknowledgement_waits_is_seen_by_the_recheck(bool loseAccess, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (_, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var targetContext = await OwnershipSqlSeed.SeedContextAsync(scratch, "Acknowledgement move target");
        var observed = new List<string>();
        var before = await SnapshotAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "Integrations", integrationId);
        var acknowledgement = RunAsync(() => AckAsync(
            scratch,
            principal,
            integrationId,
            incidentId,
            NoteSentinel,
            async _ =>
            {
                // The integration as it is when the check runs: after the lock is held, so after the move committed.
                var owner = await scratch.ScalarAsync<string>($"SELECT ResourceContextId FROM Integrations WHERE Id = N'{integrationId}';");
                observed.Add(owner);
                if (loseAccess && owner == targetContext)
                    throw new UnauthorizedAccessException("The caller no longer holds integration.update on the integration.");
            }));
        await holder.WaitUntilBlockedAsync(scratch, "Integrations", acknowledgement);

        // An ownership move commits while the acknowledgement waits for the integration row.
        await holder.ExecuteAsync($"UPDATE Integrations SET ResourceContextId = N'{targetContext}' WHERE Id = N'{integrationId}';");
        await holder.CommitAsync();
        var outcome = await acknowledgement;

        observed.Should().ContainSingle("the check ran once, under the lock, against the integration as moved").Which.Should().Be(targetContext);
        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        if (loseAccess)
        {
            outcome.Error.Should().BeOfType<UnauthorizedAccessException>();
            (await SnapshotAsync(scratch)).Should().Be(before, "a refused acknowledgement changes nothing");
            rows.Should().BeEmpty("a refused acknowledgement records nothing");
        }
        else
        {
            outcome.Error.Should().BeNull();
            outcome.Value.Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);
            (await SnapshotAsync(scratch)).Should().NotBe(before);
            rows.Should().ContainSingle().Which["ResourceContextId"].Should().Be(targetContext, "the row names the owner the unit locked, not the one the caller first read");
        }

        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- the observation worker's resolution ----

    private static ScheduleExpectationSnapshot SatisfiedSnapshot() =>
        new(
            new ScheduleExpectationEvaluation(
                ExpectsRecurringRuns: true,
                HasExpectedFireInWindow: true,
                ExpectedFireCountInWindow: 1,
                MostRecentExpectedFireUtc: null,
                InGracePeriod: false,
                SatisfiedByAuthorizedScheduleOriginRun: false,
                LastSatisfyingScheduleOriginRunId: null,
                LastSatisfyingScheduleOriginRunAt: null,
                Status: ScheduleExpectationEvaluationStatus.Satisfied,
                OperatorSummaryLine: "satisfied"),
            ReferenceScheduleId: "sch1",
            LatestScheduleOriginAuthorizedRun: null,
            EvidenceWindowStartUtc: DateTimeOffset.Parse("2025-01-01T00:00:00Z"));

    private static Task<Outcome<AttentionIncidentObserveTransition>> ResolveAsync(AuditSqlScratch scratch, string integrationId) =>
        RunAsync(async () =>
        {
            await using var db = scratch.NewDb();
            var result = await new SqlAttentionIncidentTracker(db)
                .ObserveScheduleExpectationAsync(new IntegrationId(integrationId), SatisfiedSnapshot(), DateTimeOffset.UtcNow);
            return result.Transition;
        });

    /// <summary>
    /// An acknowledgement and the observation worker's resolution both queue on the incident row while a third session holds it,
    /// in both arrival orders. The worker does not take the integration row, so the incident row is where they meet.
    /// <list type="bullet">
    /// <item>Acknowledgement first: it commits its columns and its row, and the resolution then writes only the columns it
    /// changed. The incident ends resolved and acknowledged; the acknowledgement is not lost.</item>
    /// <item>Resolution first: the acknowledgement reads the incident resolved under its lock and is refused as not active. Nothing
    /// is written and no row exists.</item>
    /// </list>
    /// </summary>
    [Theory]
    [MemberData(nameof(ArrivalOrders))]
    public async Task An_acknowledgement_that_races_the_resolution_ends_acknowledged_then_resolved_or_as_a_refusal_and_never_loses_the_columns(
        bool acknowledgementFirst, bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var lastAudit = await LastAuditIdAsync(scratch);

        await using var holder = await LockedRowHolder.TakeAsync(scratch.ConnectionString, "AttentionIncidents", incidentId);
        Task<Outcome<AttentionIncidentAcknowledgementOutcome>> Acknowledge() =>
            RunAsync(() => AckAsync(scratch, principal, integrationId, incidentId, "raced note"));

        Task<Outcome<AttentionIncidentAcknowledgementOutcome>> acknowledgement;
        Task<Outcome<AttentionIncidentObserveTransition>> resolution;
        if (acknowledgementFirst)
        {
            acknowledgement = Acknowledge();
            await holder.WaitUntilBlockedAsync(scratch, "AttentionIncidents", acknowledgement);
            resolution = ResolveAsync(scratch, integrationId);
            await holder.WaitUntilBlockedAsync(scratch, "AttentionIncidents", Task.WhenAny(acknowledgement, resolution), minimumWaiters: 2);
        }
        else
        {
            resolution = ResolveAsync(scratch, integrationId);
            await holder.WaitUntilBlockedAsync(scratch, "AttentionIncidents", resolution);
            acknowledgement = Acknowledge();
            await holder.WaitUntilBlockedAsync(scratch, "AttentionIncidents", Task.WhenAny(acknowledgement, resolution), minimumWaiters: 2);
        }

        await holder.CommitAsync();
        var acknowledged = await acknowledgement;
        var resolved = await resolution;

        acknowledged.Error.Should().BeNull();
        resolved.Error.Should().BeNull();
        resolved.Value.Should().Be(AttentionIncidentObserveTransition.Resolved);
        var incident = await IncidentAsync(scratch, incidentId);
        incident["Status"].Should().Be(AttentionIncidentStatuses.Resolved, "the worker's resolution is never lost either");
        var rows = await AuditRowsAfterAsync(scratch, lastAudit);
        if (acknowledgementFirst)
        {
            acknowledged.Value.Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged);
            incident["AcknowledgedBy"].Should().Be(admin, "the resolution wrote only the columns it changed");
            incident["AcknowledgementNote"].Should().Be("raced note");
            incident["AcknowledgedAtUtc"].Should().NotBeNull();
            rows.Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.AttentionIncidentAcknowledged);
        }
        else
        {
            acknowledged.Value.Should().Be(AttentionIncidentAcknowledgementOutcome.NotActive);
            incident["AcknowledgedAtUtc"].Should().BeNull();
            incident["AcknowledgedBy"].Should().BeNull();
            incident["AcknowledgementNote"].Should().BeNull();
            rows.Should().BeEmpty();
        }

        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- what the notification pipeline reads ----

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task The_tracker_projection_the_notifications_read_shows_the_acknowledgement(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);

        await AckAsync(scratch, principal, integrationId, incidentId, "seen");

        await using var db = scratch.NewDb();
        var projection = await new SqlAttentionIncidentTracker(db).GetIncidentAsync(new IntegrationId(integrationId), incidentId);
        projection.Should().NotBeNull();
        projection!.AcknowledgedAtUtc.Should().NotBeNull();
        projection.AcknowledgedBy.Should().Be(admin);
        projection.AcknowledgementNote.Should().Be("seen");
        _output.WriteLine(scratch.ExecutionMarker);
    }

    // ---- M6 ----

    private async Task<(AuditedOperationM6Runner Runner, IPrincipalContext Principal)> RunnerAsync(AuditSqlScratch scratch)
    {
        var (_, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var runner = new AuditedOperationM6Runner(
            scratch,
            () => SnapshotAsync(scratch),
            AuditActionNames.AttentionIncidentAcknowledged,
            async (db, unit) => (await Service(db, unit).AcknowledgeAsync(new IntegrationId(integrationId), incidentId, NoteSentinel, Permitted))
                .Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged),
            NoteSentinel);
        return (runner, principal);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_audit_insert_failure_leaves_the_incident_exactly_as_it_was(bool rcsi)
    {
        await using var scratch = await NewScratchAsync(rcsi);
        var (runner, principal) = await RunnerAsync(scratch);
        await runner.AuditFailureLeavesNothingAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_lost_acknowledgement_is_verified_and_reconciled_without_running_the_attempt_again(bool rcsi)
    {
        await using var scratch = await NewScratchAsync(rcsi);
        var (runner, principal) = await RunnerAsync(scratch);
        await runner.LostAcknowledgementIsVerifiedAndReconciledAsync(principal);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_acknowledgement_whose_commit_acknowledgement_was_lost_answers_acknowledged_with_the_columns_and_one_row(bool rcsi)
    {
        await using var scratch = await NewScratchAsync(rcsi);
        var (admin, principal) = await AdminAsync(scratch);
        var (integrationId, incidentId) = await ArrangeAsync(scratch);
        var lost = new LostCommitAcknowledgement();
        var attempts = new AttemptCounter();

        AttentionIncidentAcknowledgementOutcome outcome;
        await using (var db = scratch.NewDb(lost, attempts))
            outcome = await Service(db, NewUnit(db, principal)).AcknowledgeAsync(new IntegrationId(integrationId), incidentId, "kept", Permitted);

        outcome.Should().Be(AttentionIncidentAcknowledgementOutcome.Acknowledged, "a verified commit is the acknowledgement having happened");
        attempts.Started.Should().Be(1, "a verified commit is not run again");
        var incident = await IncidentAsync(scratch, incidentId);
        incident["AcknowledgedBy"].Should().Be(admin);
        incident["AcknowledgementNote"].Should().Be("kept");
        (await AuditRowsAfterAsync(scratch, 0)).Should().ContainSingle().Which["Action"].Should().Be(AuditActionNames.AttentionIncidentAcknowledged);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task A_confirmed_rollback_reruns_the_acknowledgement_once_and_ends_with_one_change_and_one_row(bool rcsi)
    {
        await using var scratch = await NewScratchAsync(rcsi);
        var (runner, principal) = await RunnerAsync(scratch);
        await runner.ConfirmedRollbackRerunsOnceAsync(principal);
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public async Task An_unknown_outcome_is_wholly_committed_or_wholly_absent(bool committed, bool rcsi)
    {
        await using var scratch = await NewScratchAsync(rcsi);
        var (runner, principal) = await RunnerAsync(scratch);
        await runner.UnknownOutcomeIsWhollyCommittedOrWhollyAbsentAsync(principal, committed);
    }

    [Theory]
    [MemberData(nameof(Isolation))]
    public async Task An_operation_lock_that_cannot_be_taken_is_unknown_and_changes_nothing(bool rcsi)
    {
        await using var scratch = await NewScratchAsync(rcsi);
        var (runner, principal) = await RunnerAsync(scratch);
        await runner.OperationLockNotGrantedIsUnknownAndChangesNothingAsync(principal);
    }
}
