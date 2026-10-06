using System.Diagnostics;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The startup classification step against SQL Server (audit hardening design gate §15.7; A1.1-8, A1.1-20): evidence is
/// byte-identical afterwards, every user id exists, unresolved and ambiguous rows carry none, a rerun changes nothing, an
/// interrupted run converges, concurrent classifiers do not overwrite each other, the <c>unknown</c> protection holds
/// against a user named <c>unknown</c>, defects apply to non-recorder rows only, historical blocked rows stay visible, and
/// provenance and outcome codes follow §11.5. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AuditClassificationSqlServerTests
{
    private const string Alice = "a11ce0000000000000000001";
    private const string Erin = "e1110000000000000000000e";
    private const string UnknownUser = "0000000000000000000000ff";
    private const string ErinOid = "3f1d2c4b-1111-4a5b-9c8d-000000000001";
    private const string SharedOid = "3f1d2c4b-2222-4a5b-9c8d-000000000002";

    private const string EvidenceColumns =
        "Id, EventId, OperationEventId, At, Actor, Action, Details, DetailsFormat, Outcome, ModuleId, ScheduleId, RunId, ResourceContextId, CorrelationId, Producer";

    private readonly ITestOutputHelper _output;

    public AuditClassificationSqlServerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Classification_completes_every_legacy_row_by_the_gate_rules_and_leaves_evidence_byte_identical()
    {
        await using var db = await SeedAsync();
        var evidenceBefore = await db.RowsAsync($"SELECT {EvidenceColumns} FROM AuditLogs ORDER BY Id;");
        var recorderBefore = await db.RowsAsync("SELECT * FROM AuditLogs WHERE Producer = N'recorder';");

        await using (var ctx = db.NewDb())
        {
            var report = await AuditClassification.RunAsync(ctx, NullLogger.Instance, options: new AuditClassificationOptions { BatchSize = 4 });
            report.Completed.Should().Be(SeededUnclassified);
            WriteReport(report);
        }

        (await db.RowsAsync($"SELECT {EvidenceColumns} FROM AuditLogs ORDER BY Id;")).Should().BeEquivalentTo(
            evidenceBefore, o => o.WithStrictOrdering(), "classification never writes an evidence column");
        (await db.RowsAsync("SELECT * FROM AuditLogs WHERE Producer = N'recorder';")).Should().BeEquivalentTo(recorderBefore);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ClassificationState IS NULL;")).Should().Be(0);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs a WHERE a.ActorUserId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM Users u WHERE u.Id = a.ActorUserId);"))
            .Should().Be(0, "every ActorUserId exists");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ActorKind = N'unresolved' AND ActorUserId IS NOT NULL;")).Should().Be(0);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ActorExternalProvider IS NOT NULL OR ActorKind = N'external';"))
            .Should().Be(0, "no historical row is classified external");

        var rows = (await db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;")).ToDictionary(r => (string)r["Details"]!);

        // §15.2 actor rules.
        Expect(rows["r:user-id"], kind: "user", userId: Alice, username: null, attribution: "recorded_user_id");
        Expect(rows["r:username"], kind: "user", userId: Alice, username: "alice.smith", attribution: "inferred_username");
        Expect(rows["r:entra"], kind: "user", userId: Erin, username: null, attribution: "inferred_entra_object_id");
        Expect(rows["r:shared-oid"], kind: "unresolved", userId: null, username: null, attribution: "ambiguous");
        Expect(rows["r:nobody"], kind: "unresolved", userId: null, username: null, attribution: "unresolved");
        Expect(rows["r:literal"], kind: "system", userId: null, username: null, attribution: "recorded_literal");
        Expect(rows["r:worker"], kind: "worker", userId: null, username: null, attribution: "recorded_literal");
        Expect(rows["r:anonymous"], kind: "anonymous", userId: null, username: null, attribution: "recorded_literal");
        // The audit-only protection: a user really named "unknown" is never credited with the placeholder.
        Expect(rows["r:unknown"], kind: "unresolved", userId: null, username: null, attribution: "unresolved");

        // §11.5 / A1.1-20 outcome provenance, raw values untouched.
        Outcome(rows["r:pre37-not-invariant-failed"], raw: "failed", provenance: "indeterminate", code: "unknown");
        Outcome(rows["r:post37-failed"], raw: "failed", provenance: "recorded", code: "failed");
        Outcome(rows["r:pre37-invariant-failed"], raw: "failed", provenance: "indeterminate", code: "failed");
        Outcome(rows["r:category-like"], raw: "auth-admin", provenance: "indeterminate", code: "unknown");
        Outcome(rows["r:unknown-action"], raw: "succeeded", provenance: "indeterminate", code: "unknown");
        rows["r:unknown-action"]["Category"].Should().Be("other");
        rows["r:unknown-action"]["Basis"].Should().Be("unspecified");

        // §11.4 visibility: routine dispatches hidden by default; every historical blocked row stays visible.
        rows["r:dispatched"]["Visibility"].Should().Be("routine");
        rows["r:dispatched"]["OutcomeCode"].Should().Be("succeeded");
        rows["r:blocked-1"]["Visibility"].Should().Be("default");
        rows["r:blocked-2"]["Visibility"].Should().Be("default", "consecutive blocked rows never prove continuity (AC-3)");
        rows["r:blocked-2"]["OutcomeCode"].Should().Be("blocked");

        // §15.3 defects, on non-recorder rows only.
        Issue(rows["r:invitation"], "subject_recorded_as_actor", "definite");
        Issue(rows["r:retry-system"], "initiator_not_recorded", "definite");
        Issue(rows["r:retry-person"], null, null);
        Issue(rows["r:append-invitation"], "subject_recorded_as_actor", "definite");
        rows["r:append-invitation"]["OutcomeProvenance"].Should().Be("recorded", "a row with a producer recorded its own outcome");

        // §12.6 P-5 targets: only an exact typed JSON document supplies one.
        var exact = rows.Single(r => r.Key.Contains("\"conn-1\"", StringComparison.Ordinal)).Value;
        exact["TargetType"].Should().Be("connection");
        exact["TargetId"].Should().Be("conn-1");
        rows.Single(r => r.Key.Contains("\"conn-2\"", StringComparison.Ordinal)).Value["TargetType"]
            .Should().BeNull("a Resource= suffix means the details are not exactly the typed document");
        _output.WriteLine(db.ExecutionMarker);
    }

    [Fact]
    public async Task A_rerun_changes_zero_rows_and_a_run_with_nothing_to_classify_takes_under_a_second()
    {
        await using var db = await SeedAsync();
        await using (var ctx = db.NewDb())
            await AuditClassification.RunAsync(ctx, NullLogger.Instance);
        var after = await db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

        await using (var ctx = db.NewDb())
        {
            var stopwatch = Stopwatch.StartNew();
            var rerun = await AuditClassification.RunAsync(ctx, NullLogger.Instance);
            stopwatch.Stop();
            rerun.Read.Should().Be(0);
            rerun.Completed.Should().Be(0);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
            _output.WriteLine($"Empty classification pass: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        }

        (await db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;")).Should().BeEquivalentTo(after, o => o.WithStrictOrdering());
        _output.WriteLine(db.ExecutionMarker);
    }

    [Fact]
    public async Task An_interrupted_run_keeps_its_committed_batches_and_the_next_run_converges()
    {
        await using var db = await SeedAsync();
        var batches = 0;

        await using (var ctx = db.NewDb())
        {
            var interrupted = () => AuditClassification.RunAsync(ctx, NullLogger.Instance, options: new AuditClassificationOptions
            {
                BatchSize = 3,
                OnBatchCommitted = _ =>
                {
                    if (++batches == 2)
                        throw new InvalidOperationException("Injected: the host stopped mid-run.");
                },
            });
            await interrupted.Should().ThrowAsync<InvalidOperationException>();
        }

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ClassificationState = N'complete' AND Producer IS NULL;"))
            .Should().Be(6, "two batches of three committed before the interruption");

        await using (var ctx = db.NewDb())
        {
            var resumed = await AuditClassification.RunAsync(ctx, NullLogger.Instance, options: new AuditClassificationOptions { BatchSize = 3 });
            resumed.Completed.Should().Be(SeededUnclassified - 6);
        }

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ClassificationState IS NULL;")).Should().Be(0);
        _output.WriteLine(db.ExecutionMarker);
    }

    [Fact]
    public async Task Concurrent_classifiers_complete_each_row_exactly_once_without_overwriting_each_other()
    {
        await using var db = await SeedAsync();

        await using var first = db.NewDb();
        await using var second = db.NewDb();
        var options = new AuditClassificationOptions { BatchSize = 2, LockWait = TimeSpan.Zero };
        var results = await Task.WhenAll(
            AuditClassification.RunAsync(first, NullLogger.Instance, options: options),
            AuditClassification.RunAsync(second, NullLogger.Instance, options: options));

        results.Sum(r => r.Completed).Should().Be(SeededUnclassified, "each row is completed by exactly one conditional transition");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ClassificationState IS NULL;")).Should().Be(0);
        _output.WriteLine($"Concurrent classifiers: completed {results[0].Completed} and {results[1].Completed}; locks {results[0].LockAcquired}/{results[1].LockAcquired}");
        _output.WriteLine(db.ExecutionMarker);
    }

    [Fact]
    public async Task A_preview_reports_counts_and_changes_nothing()
    {
        await using var db = await SeedAsync();
        var before = await db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;");

        await using (var ctx = db.NewDb())
        {
            var preview = await AuditClassification.PreviewAsync(ctx, NullLogger.Instance);
            preview.Applied.Should().BeFalse();
            preview.Read.Should().Be(SeededUnclassified);
            preview.ByAttribution["recorded_literal"].Should().BeGreaterThan(0);
            WriteReport(preview);
        }

        (await db.RowsAsync("SELECT * FROM AuditLogs ORDER BY Id;")).Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
        _output.WriteLine(db.ExecutionMarker);
    }

    // ---- seeding ----

    /// <summary>Legacy (producer-less) rows, one append row inserted unclassified, and one complete recorder row.</summary>
    private const int SeededUnclassified = 24;

    private async Task<AuditSqlScratch> SeedAsync()
    {
        var db = await AuditSqlScratch.CreateAsync("Apperture_Aud11Classify_");
        await db.AddUserAsync(Alice, "alice.smith");
        await db.AddUserAsync(Erin, "erin.jones");
        await db.AddUserAsync(UnknownUser, "unknown");
        await db.AddUserAsync("b0b000000000000000000001", "bob.one");
        await db.AddUserAsync("b0b000000000000000000002", "bob.two");
        await db.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt) VALUES
                (N'le1', N'{Erin}', 0, N'{ErinOid}', N'tenant-1', SYSDATETIMEOFFSET()),
                (N'ls1', N'b0b000000000000000000001', 0, N'{SharedOid}', N'tenant-1', SYSDATETIMEOFFSET()),
                (N'ls2', N'b0b000000000000000000002', 0, N'{SharedOid}', N'tenant-2', SYSDATETIMEOFFSET());
            """);

        // Details carry a unique label so each row can be found; it is evidence and must not change.
        var legacy = new (string Actor, string Action, string? Outcome, string Details)[]
        {
            (Alice, "user.created", "auth-admin", "r:user-id"),
            ("alice.smith", "identity_link.added", "auth-admin", "r:username"),
            (ErinOid, "identity_link.added", "auth-admin", "r:entra"),
            (SharedOid, "identity_link.added", "auth-admin", "r:shared-oid"),
            ("nobody.known", "identity_link.added", "auth-admin", "r:nobody"),
            ("system", "runtime.composition.invalid_at_startup", "Degraded", "r:literal"),
            ("system-worker", "execution.workflow_run.failed_definition_unavailable", "failed", "r:pre37-not-invariant-failed"),
            ("anonymous", "auth.local.login.failed", "auth", "r:anonymous"),
            ("unknown", "execution.operation_item.bulk_retry.requested", "success", "r:unknown"),
            ("system-worker", "execution.operation_run.failed_definition_unavailable", "Blocked", "r:worker"),
            (Alice, "connection.verified", "failed", "r:post37-failed"),
            ("system:scheduler", "scheduler.schedule.failed", "failed", "r:pre37-invariant-failed"),
            (Alice, "governance.permission.assignment.updated", "auth-admin", "r:category-like"),
            (Alice, "role.assigned", "succeeded", "r:unknown-action"),
            ("system:scheduler", "scheduler.schedule.dispatched", "success", "r:dispatched"),
            ("system:scheduler", "scheduler.schedule.blocked_by_lifecycle", "blocked", "r:blocked-1"),
            ("system:scheduler", "scheduler.schedule.blocked_by_lifecycle", "blocked", "r:blocked-2"),
            (Alice, "account.invitation.sent", "account", "r:invitation"),
            ("system", "execution.workflow_retry.blocked", "Blocked", "r:retry-system"),
            (Alice, "execution.workflow_retry.blocked", "Blocked", "r:retry-person"),
            (Alice, "connection.created", "succeeded",
                "{\"resourceType\":\"Connection\",\"resourceId\":\"conn-1\",\"displayName\":\"Ledger\",\"connectorDefinitionId\":\"sql\"}"),
            (Alice, "connection.updated", "succeeded",
                "{\"resourceType\":\"Connection\",\"resourceId\":\"conn-2\",\"displayName\":null,\"connectorDefinitionId\":\"sql\"}; Resource=Connection/instance"),
            (Alice, "auth.local.login.success", "auth", "r:login"),
        };
        foreach (var (actor, action, outcome, details) in legacy)
        {
            await db.ExecuteAsync(
                $"INSERT INTO AuditLogs (At, Actor, Action, Outcome, Details) VALUES (SYSDATETIMEOFFSET(), N'{actor}', N'{action}', {Sql(outcome)}, N'{details.Replace("'", "''")}');");
        }

        // An append row whose insert-time classification could not run: inserted wholly unclassified.
        await db.ExecuteAsync($"""
            INSERT INTO AuditLogs (At, Actor, Action, Outcome, Details, EventId, OperationEventId, Producer)
            VALUES (SYSDATETIMEOFFSET(), N'{Alice}', N'account.invitation.sent', N'account', N'r:append-invitation', N'append-1', N'append-1', N'append');
            """);

        // A complete recorder row: never touched by classification.
        await db.ExecuteAsync($"""
            INSERT INTO AuditLogs (At, Actor, Action, Outcome, Details, EventId, OperationEventId, Producer, DetailsFormat, ClassificationState,
                ActorKind, ActorUserId, ActorUsername, ActorAttribution, TargetType, TargetId, Category, Severity, Visibility, Basis, OutcomeCode, OutcomeProvenance)
            VALUES (SYSDATETIMEOFFSET(), N'{Alice}', N'governance.user.username.changed', N'succeeded', N'r:recorder', N'rec-1', N'rec-1', N'recorder', N'json/1',
                N'complete', N'user', N'{Alice}', N'alice.smith', N'recorded', N'user', N'{Erin}', N'identity', N'elevated', N'default', N'change', N'succeeded', N'recorded');
            """);

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE ClassificationState IS NULL;")).Should().Be(SeededUnclassified);
        return db;
    }

    private static string Sql(string? value) => value is null ? "NULL" : $"N'{value}'";

    private static void Expect(Dictionary<string, object?> row, string kind, string? userId, string? username, string attribution)
    {
        row["ClassificationState"].Should().Be("complete");
        row["ActorKind"].Should().Be(kind, (string)row["Details"]!);
        row["ActorUserId"].Should().Be(userId, (string)row["Details"]!);
        row["ActorUsername"].Should().Be(username, (string)row["Details"]!);
        row["ActorAttribution"].Should().Be(attribution, (string)row["Details"]!);
    }

    private static void Outcome(Dictionary<string, object?> row, string raw, string provenance, string code)
    {
        row["Outcome"].Should().Be(raw, "the raw value is never rewritten");
        row["OutcomeProvenance"].Should().Be(provenance, (string)row["Details"]!);
        row["OutcomeCode"].Should().Be(code, (string)row["Details"]!);
    }

    private static void Issue(Dictionary<string, object?> row, string? code, string? certainty)
    {
        row["AttributionIssue"].Should().Be(code, (string)row["Details"]!);
        row["AttributionIssueCertainty"].Should().Be(certainty, (string)row["Details"]!);
    }

    private void WriteReport(AuditClassificationReport report)
    {
        _output.WriteLine($"Applied={report.Applied} Read={report.Read} Completed={report.Completed} Batches={report.Batches} Duration={report.Duration}");
        foreach (var (name, counts) in new[]
        {
            ("Kind", report.ByKind), ("Attribution", report.ByAttribution), ("Issue", report.ByIssue),
            ("OutcomeProvenance", report.ByOutcomeProvenance), ("OutcomeCode", report.ByOutcomeCode), ("Visibility", report.ByVisibility),
        })
        {
            _output.WriteLine($"  {name}: {string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"))}");
        }
    }
}
