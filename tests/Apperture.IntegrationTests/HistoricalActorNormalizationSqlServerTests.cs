using System.Data;
using Apperture.Core.Application.Services;
using Apperture.IntegrationTests.ManagementHost;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Apperture.IntegrationTests;

/// <summary>
/// Real SQL Server proof for historical actor normalization (IAM-9.2a): every approved column is populated with every
/// kind of historical value, normalized, and checked row by row; a second run changes nothing; values an earlier binary
/// writes after a rollback are normalized by the next run; an interrupted run converges; the audit log and the identity
/// tables are never touched; and the Management Host runs it at startup.
/// </summary>
/// <remarks>
/// Each test uses its own throwaway catalog. Rows are inserted with only the actor columns meaningful: every other
/// required column gets a type-appropriate filler, and the target tables' foreign-key and check constraints are disabled
/// in that catalog only. The identity tables keep all their constraints. Without a reachable SQL endpoint the tests
/// fail (see <see cref="SqlTestExecution"/>).
/// </remarks>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class HistoricalActorNormalizationSqlServerTests
{
    // ---- identities ---------------------------------------------------------------------------------------------
    private const string Pat = "0a0000000000000000000001";
    private const string Erin = "0a0000000000000000000002";
    private const string UserA = "3f9a1c0e7b2d4a6f8e1c5b7d";
    private const string Bob = "0a0000000000000000000004";
    private const string Cara = "0a0000000000000000000005";
    private const string Dan = "0a0000000000000000000006";
    private const string Eve = "0a0000000000000000000007";
    private const string Finn = "0a0000000000000000000008";
    private const string Gus = "0a0000000000000000000009";
    private const string Ivy = "0a000000000000000000000a";
    private const string Jon = "0a000000000000000000000b";

    private const string TenantOne = "aaaaaaaa-0000-4000-8000-000000000001";
    private const string TenantTwo = "aaaaaaaa-0000-4000-8000-000000000002";
    private const string ErinOid = "11111111-1111-4111-8111-111111111111";
    private const string SharedOid = "22222222-2222-4222-8222-222222222222"; // Cara (tenant one) and Dan (tenant two)
    private const string EveOid = "33333333-3333-4333-8333-333333333333"; // Eve, in both tenants
    private const string GusName = "44444444-4444-4444-8444-444444444444"; // Gus's grandfathered username and his own object id
    private const string IvyJon = "55555555-5555-4555-8555-555555555555"; // Ivy's grandfathered username, Jon's object id
    private const string UnknownOid = "99999999-9999-4999-8999-999999999999";

    /// <summary>The oracle: what each historical value is, and what it must be afterwards.</summary>
    private static readonly Dictionary<string, (HistoricalActorCategory Category, string Final)> Expected = new(StringComparer.Ordinal)
    {
        ["pat.lee"] = (HistoricalActorCategory.Resolved, Pat), // current username
        ["pat.old"] = (HistoricalActorCategory.Resolved, Pat), // retired username
        [ErinOid] = (HistoricalActorCategory.Resolved, Erin), // unique Entra object id
        [Pat] = (HistoricalActorCategory.AlreadyUserId, Pat),
        ["system"] = (HistoricalActorCategory.Literal, "system"),
        ["system:scheduler"] = (HistoricalActorCategory.Literal, "system:scheduler"),
        ["ghost.user"] = (HistoricalActorCategory.Unresolved, "ghost.user"),
        [UnknownOid] = (HistoricalActorCategory.Unresolved, UnknownOid),
        [UserA] = (HistoricalActorCategory.Ambiguous, UserA), // A's id and Bob's retired grandfathered username
        ["ann"] = (HistoricalActorCategory.Ambiguous, "ann"), // only A, but A's id is ambiguous: never written
        [SharedOid] = (HistoricalActorCategory.Ambiguous, SharedOid), // object id of two users
        [EveOid] = (HistoricalActorCategory.Unresolved, EveOid), // two links of one user: the gate needs exactly one
        [Finn] = (HistoricalActorCategory.AlreadyUserId, Finn), // id and username of the same user
        [GusName] = (HistoricalActorCategory.Resolved, Gus), // username and object id of the same user
        [IvyJon] = (HistoricalActorCategory.Ambiguous, IvyJon), // username of one user, object id of another
        ["PAT.LEE"] = (HistoricalActorCategory.Resolved, Pat), // usernames are case-insensitive
        ["pat.lee "] = (HistoricalActorCategory.Unresolved, "pat.lee "), // byte-exact: a trailing space is another value
    };

    private static readonly string[] EveryCase =
    [
        "pat.lee", "pat.old", ErinOid, Pat, "system", "system:scheduler", "ghost.user", UnknownOid,
        UserA, "ann", SharedOid, EveOid, Finn, GusName, IvyJon,
    ];

    // -------------------------------------------------------------------------------------------------------------
    // Full corpus
    // -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_approved_column_is_normalized_by_the_gate_rules_and_counted_in_rows()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aCorpus_");
        var identityBefore = await IdentitySnapshotAsync(scratch.ConnectionString);

        HistoricalActorNormalizationReport report;
        await using (var db = scratch.NewDb())
            report = await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);

        report.Applied.Should().BeTrue();
        AssertCounts(report, scratch.Corpus, afterNormalization: false);
        await AssertFinalStateAsync(scratch);
        (await IdentitySnapshotAsync(scratch.ConnectionString)).Should().Equal(identityBefore, "no user, link or ledger row is created or changed");
        (await ScalarAsync<string>(scratch.ConnectionString, "SELECT [Actor] FROM AuditLogs;"))
            .Should().Be("pat.lee", "AuditLogs.Actor is never rewritten");
    }

    [Fact]
    public async Task A_second_run_changes_nothing_and_reports_the_normalized_state()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aRerun_");

        await using (var db = scratch.NewDb())
            await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);
        var afterFirst = await AllActorValuesAsync(scratch.ConnectionString);
        var identityAfterFirst = await IdentitySnapshotAsync(scratch.ConnectionString);

        HistoricalActorNormalizationReport second;
        await using (var db = scratch.NewDb())
            second = await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);

        second.Resolved.Should().Be(0, "nothing is left to rewrite");
        AssertCounts(second, scratch.Corpus, afterNormalization: true);
        (await AllActorValuesAsync(scratch.ConnectionString)).Should().BeEquivalentTo(afterFirst);
        (await IdentitySnapshotAsync(scratch.ConnectionString)).Should().Equal(identityAfterFirst);
    }

    [Fact]
    public async Task Legacy_values_written_again_after_a_rollback_are_normalized_by_the_next_run()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aReforward_");
        await using (var db = scratch.NewDb())
            await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);

        // An earlier binary, running after a rollback, writes usernames and object ids again.
        var column = new HistoricalActorColumn("RuntimeControlRequests", "RequestedBy");
        foreach (var value in new[] { "pat.lee", ErinOid, "system", "ghost.user" })
            await scratch.InsertActorRowAsync(column.Table, new Dictionary<string, string?> { [column.Column] = value });

        HistoricalActorNormalizationReport rerun;
        await using (var db = scratch.NewDb())
            rerun = await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);

        var result = rerun.Columns.Single(c => c.Column == column);
        result.Resolved.Should().Be(2, "the reintroduced username and object id");
        var values = await ColumnValuesAsync(scratch.ConnectionString, column);
        values.Should().NotContain(["pat.lee", ErinOid]);
        values.Count(v => v == "system").Should().Be(2, "literals stay");
        values.Count(v => v == "ghost.user").Should().Be(2, "unresolved values stay");
    }

    [Fact]
    public async Task An_interrupted_run_converges_to_the_same_result_as_an_uninterrupted_one()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aInterrupt_");

        var completed = 0;
        var interrupted = async () =>
        {
            await using var db = scratch.NewDb();
            await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance, onColumnCompleted: _ =>
            {
                if (++completed == 5)
                    throw new InvalidOperationException("Simulated crash after five columns.");
            });
        };
        await interrupted.Should().ThrowAsync<InvalidOperationException>();

        // Each rewrite committed on its own: the first five columns are done, every later one is untouched.
        var done = HistoricalActorNormalization.Columns.Take(5).ToList();
        var pending = HistoricalActorNormalization.Columns.Skip(5).ToList();
        foreach (var column in done)
            await AssertColumnStateAsync(scratch, column, normalized: true);
        foreach (var column in pending)
            await AssertColumnStateAsync(scratch, column, normalized: false);

        HistoricalActorNormalizationReport resumed;
        await using (var db = scratch.NewDb())
            resumed = await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);

        await AssertFinalStateAsync(scratch);
        resumed.Resolved.Should().Be(
            pending.Sum(c => ExpectedValues(scratch, c, normalized: false).Count(v => v is not null && Expected[v].Category == HistoricalActorCategory.Resolved)),
            "the resumed run rewrites exactly the columns the interrupted run had not reached");
        foreach (var result in resumed.Columns)
        {
            result.DistinctValues.Should().Be(
                ExpectedValues(scratch, result.Column, normalized: done.Contains(result.Column))
                    .Where(v => v is not null).Distinct(StringComparer.Ordinal).Count(),
                $"{result.Column} distinct values seen by the resumed run");
        }

        resumed.Ambiguous.Should().Be(
            scratch.Corpus.Sum(r => r.Values.Values.Count(v => v is not null && Expected[v].Category == HistoricalActorCategory.Ambiguous)),
            "no value becomes ambiguous because another column was normalized first");
    }

    [Fact]
    public async Task A_normalization_failure_fails_host_startup_keeps_committed_progress_and_converges_after_repair()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aFailClosed_", corpus: false);

        var early = HistoricalActorNormalization.Columns.Take(4).ToList();
        var broken = HistoricalActorNormalization.Columns[4];
        var late = HistoricalActorNormalization.Columns[^1];
        broken.Should().Be(new HistoricalActorColumn("AttentionIncidents", "AcknowledgedBy"), "the failure is induced after four columns");
        foreach (var column in early.Append(broken).Append(late))
        {
            foreach (var value in new[] { "pat.lee", "pat.old", ErinOid, "ghost.user", UserA })
                await scratch.InsertActorRowAsync(column.Table, new Dictionary<string, string?> { [column.Column] = value });
        }

        // After migrations, a schema problem makes the fifth column unavailable.
        await ExecuteAsync(scratch.ConnectionString, "EXEC sp_rename N'dbo.AttentionIncidents.AcknowledgedBy', N'AcknowledgedByUnavailable', N'COLUMN';");

        var failedCompletion = new MigrationCompletionState();
        var failedLogs = new CapturingLoggerProvider();
        var failedStart = () => StartHostAsync(scratch.ConnectionString, failedCompletion, failedLogs);
        await failedStart.Should().ThrowAsync<Exception>("an execution failure is a failed start, not a warning");

        failedCompletion.IsComplete.Should().BeFalse("persistence is not reported ready");
        failedLogs.Messages.Should().Contain(m => m.StartsWith("Historical actor normalization failed", StringComparison.Ordinal));
        failedLogs.Messages.Should().Contain(m => m.StartsWith("Database migration failed. Application cannot start", StringComparison.Ordinal));
        failedLogs.Messages.Should().NotContain("Database migrations applied successfully.");

        // Before any repair: the four earlier columns are already committed; the broken and later columns are untouched.
        foreach (var column in early)
            await AssertColumnStateAsync(scratch, column, normalized: true);
        await AssertColumnStateAsync(scratch, late, normalized: false);
        (await ColumnValuesAsync(scratch.ConnectionString, broken with { Column = "AcknowledgedByUnavailable" }))
            .Should().BeEquivalentTo(ExpectedValues(scratch, broken, normalized: false));

        await ExecuteAsync(scratch.ConnectionString, "EXEC sp_rename N'dbo.AttentionIncidents.AcknowledgedByUnavailable', N'AcknowledgedBy', N'COLUMN';");

        var repairedCompletion = new MigrationCompletionState();
        await StartHostAsync(scratch.ConnectionString, repairedCompletion, new CapturingLoggerProvider());

        repairedCompletion.IsComplete.Should().BeTrue("the repaired start completes");
        await AssertFinalStateAsync(scratch);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Focused cases
    // -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_user_id_that_is_another_users_grandfathered_username_is_left_verbatim_as_ambiguous()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aCollision_", corpus: false);
        var column = new HistoricalActorColumn("AttentionIncidents", "AcknowledgedBy");
        await scratch.InsertActorRowAsync(column.Table, new Dictionary<string, string?> { [column.Column] = UserA });

        HistoricalActorNormalizationReport report;
        await using (var db = scratch.NewDb())
            report = await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);

        report.Columns.Single(c => c.Column == column).Ambiguous.Should().Be(1);
        (await ColumnValuesAsync(scratch.ConnectionString, column)).Should().Equal(
            [UserA], "neither treated as User A's id nor rewritten to Bob, whose retired username it is");
    }

    [Fact]
    public async Task Entra_object_ids_resolve_only_with_exactly_one_link_and_never_by_guessing_a_tenant()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aEntra_", corpus: false);
        var column = new HistoricalActorColumn("PlatformSettingValues", "UpdatedByPrincipalId");
        foreach (var value in new[] { ErinOid, SharedOid, EveOid, UnknownOid })
            await scratch.InsertActorRowAsync(column.Table, new Dictionary<string, string?> { [column.Column] = value });

        HistoricalActorNormalizationReport report;
        await using (var db = scratch.NewDb())
            report = await HistoricalActorNormalization.RunAsync(db, NullLogger.Instance);

        var result = report.Columns.Single(c => c.Column == column);
        result.Resolved.Should().Be(1, "one link: Erin");
        result.Ambiguous.Should().Be(1, "links of two users in two tenants");
        result.Unresolved.Should().Be(2, "two links of one user (the gate requires exactly one), and no link at all");
        (await ColumnValuesAsync(scratch.ConnectionString, column)).Should().BeEquivalentTo([Erin, SharedOid, EveOid, UnknownOid]);
    }

    [Fact]
    public async Task A_preview_counts_without_changing_anything()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aPreview_");
        var before = await AllActorValuesAsync(scratch.ConnectionString);

        HistoricalActorNormalizationReport preview;
        await using (var db = scratch.NewDb())
            preview = await HistoricalActorNormalization.PreviewAsync(db, NullLogger.Instance);

        preview.Applied.Should().BeFalse();
        AssertCounts(preview, scratch.Corpus, afterNormalization: false);
        (await AllActorValuesAsync(scratch.ConnectionString)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task The_management_host_normalizes_historical_actors_when_it_starts()
    {
        await using var scratch = await CreateSeededAsync("Apperture_Iam92aStartup_", corpus: false);
        var column = new HistoricalActorColumn("NotificationSubscriptions", "UpdatedByActorId");
        await scratch.InsertActorRowAsync(column.Table, new Dictionary<string, string?> { [column.Column] = "pat.old" });

        // Creating the client starts the host, which awaits every hosted service's start (migrations, then normalization)
        // before it serves. The health result itself is not the point (a bare test host reports unhealthy dependencies).
        await using (var factory = new ManagementHostWebApplicationFactory(scratch.ConnectionString))
        {
            using var client = factory.CreateClient();
            _ = await client.GetAsync("/health");
        }

        (await ColumnValuesAsync(scratch.ConnectionString, column)).Should().Equal([Pat]);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Oracle
    // -------------------------------------------------------------------------------------------------------------

    private static void AssertCounts(HistoricalActorNormalizationReport report, IReadOnlyList<CorpusRow> corpus, bool afterNormalization)
    {
        report.Columns.Select(c => c.Column).Should().Equal(HistoricalActorNormalization.Columns);
        foreach (var result in report.Columns)
        {
            var values = corpus.Where(r => r.Table == result.Column.Table).Select(r => r.Values[result.Column.Column]).ToList();
            long Count(HistoricalActorCategory category) =>
                values.Count(v => v is not null && Expected[v].Category == category);

            var expected = afterNormalization
                ? (Resolved: 0L, AlreadyUserId: Count(HistoricalActorCategory.AlreadyUserId) + Count(HistoricalActorCategory.Resolved))
                : (Resolved: Count(HistoricalActorCategory.Resolved), AlreadyUserId: Count(HistoricalActorCategory.AlreadyUserId));

            result.Resolved.Should().Be(expected.Resolved, $"{result.Column} resolved rows");
            result.AlreadyUserId.Should().Be(expected.AlreadyUserId, $"{result.Column} already-user-id rows");
            result.Literal.Should().Be(Count(HistoricalActorCategory.Literal), $"{result.Column} literal rows");
            result.Unresolved.Should().Be(Count(HistoricalActorCategory.Unresolved), $"{result.Column} unresolved rows");
            result.Ambiguous.Should().Be(Count(HistoricalActorCategory.Ambiguous), $"{result.Column} ambiguous rows");
            result.Null.Should().Be(values.Count(v => v is null), $"{result.Column} null rows");
        }
    }

    private static async Task AssertFinalStateAsync(ScratchDatabase scratch)
    {
        foreach (var column in HistoricalActorNormalization.Columns)
            await AssertColumnStateAsync(scratch, column, normalized: true);
    }

    private static async Task AssertColumnStateAsync(ScratchDatabase scratch, HistoricalActorColumn column, bool normalized) =>
        (await ColumnValuesAsync(scratch.ConnectionString, column)).Should().BeEquivalentTo(
            ExpectedValues(scratch, column, normalized),
            $"{column} {(normalized ? "after" : "before")} normalization");

    /// <summary>The column's values as seeded (legacy), or as an uninterrupted normalization leaves them.</summary>
    private static List<string?> ExpectedValues(ScratchDatabase scratch, HistoricalActorColumn column, bool normalized) =>
        scratch.Corpus
            .Where(r => r.Table == column.Table)
            .Select(r => r.Values[column.Column] is { } v ? (normalized ? Expected[v].Final : v) : null)
            .ToList();

    /// <summary>Starts the real Management Host (production startup path) and waits until it has started or failed.</summary>
    private static async Task StartHostAsync(string connectionString, MigrationCompletionState completion, CapturingLoggerProvider logs)
    {
        await using var factory = new ManagementHostWebApplicationFactory(connectionString);
        await using var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureTestServices(services => services.AddSingleton(completion));
        });
        using var client = configured.CreateClient();
        _ = await client.GetAsync("/health");
    }

    /// <summary>Records every log message (formatted), so a test can tell a failed start from a completed one.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(_messages);

        public void Dispose()
        {
        }

        private sealed class Logger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    // -------------------------------------------------------------------------------------------------------------
    // Seeding
    // -------------------------------------------------------------------------------------------------------------

    private sealed record CorpusRow(string Table, IReadOnlyDictionary<string, string?> Values);

    private static async Task<ScratchDatabase> CreateSeededAsync(string prefix, bool corpus = true)
    {
        var baseConnectionString = SqlServerTestConfiguration.RequireConnectionString();

        var catalog = prefix + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = catalog }.ConnectionString;
        SqlServerTestConfiguration.EnsureTestDatabaseExists(connectionString);

        var scratch = new ScratchDatabase(baseConnectionString, catalog, connectionString);
        await using (var db = scratch.NewDb())
            await AppertureDatabaseMigrator.MigrateAsync(db);

        foreach (var table in HistoricalActorNormalization.Columns.Select(c => c.Table).Append("AuditLogs").Distinct())
            await ExecuteAsync(connectionString, $"ALTER TABLE [{table}] NOCHECK CONSTRAINT ALL;");

        // Rows the migrations already seeded (built-in notification definitions, with no actor) are part of the oracle.
        await scratch.RecordExistingRowsAsync();

        await SeedIdentitiesAsync(scratch);
        await scratch.InsertRowAsync("AuditLogs", new Dictionary<string, object?> { ["Actor"] = "pat.lee" });

        if (corpus)
            await SeedCorpusAsync(scratch);
        return scratch;
    }

    private static async Task SeedIdentitiesAsync(ScratchDatabase scratch)
    {
        var now = DateTimeOffset.UtcNow;
        var users = new (string Id, string Username)[]
        {
            (Pat, "pat.lee"), (Erin, "erin.e"), (UserA, "ann"), (Bob, "bob.b"), (Cara, "cara"), (Dan, "dan"),
            (Eve, "eve"), (Finn, Finn), (Gus, GusName), (Ivy, IvyJon), (Jon, "jon"),
        };
        foreach (var (id, username) in users)
        {
            await scratch.InsertRowAsync("Users", new Dictionary<string, object?>
            {
                ["Id"] = id, ["Username"] = username, ["DisplayName"] = username, ["IsBreakGlassEligible"] = false,
            });
            await scratch.InsertRowAsync("UsernameLedger", new Dictionary<string, object?>
            {
                ["Username"] = username, ["UserId"] = id, ["AssignedAtUtc"] = now,
            });
        }

        foreach (var (username, owner) in new[] { ("pat.old", Pat), (UserA, Bob) })
        {
            await scratch.InsertRowAsync("UsernameLedger", new Dictionary<string, object?>
            {
                ["Username"] = username, ["UserId"] = owner, ["AssignedAtUtc"] = now.AddDays(-30),
                ["RetiredAtUtc"] = now.AddDays(-1), ["RetiredReason"] = "rename",
            });
        }

        await InsertLinkAsync(scratch, Pat, source: 1, "pat.lee", tenantId: null);
        await InsertLinkAsync(scratch, Erin, source: 0, ErinOid, TenantOne);
        await InsertLinkAsync(scratch, Cara, source: 0, SharedOid, TenantOne);
        await InsertLinkAsync(scratch, Dan, source: 0, SharedOid, TenantTwo);
        await InsertLinkAsync(scratch, Eve, source: 0, EveOid, TenantOne);
        await InsertLinkAsync(scratch, Eve, source: 0, EveOid, TenantTwo);
        await InsertLinkAsync(scratch, Gus, source: 0, GusName, TenantOne);
        await InsertLinkAsync(scratch, Jon, source: 0, IvyJon, TenantOne);
    }

    private static Task InsertLinkAsync(ScratchDatabase scratch, string userId, int source, string externalId, string? tenantId) =>
        scratch.InsertRowAsync("IdentityLinks", new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid().ToString("N")[..24],
            ["UserId"] = userId,
            ["Source"] = source,
            ["ExternalId"] = externalId,
            ["TenantId"] = tenantId,
            ["LinkedAt"] = DateTimeOffset.UtcNow,
        });

    /// <summary>
    /// For every column: one row per case, three more "pat.lee" rows (twenty in one column) so counts are rows, not
    /// values, a null where the column allows it, and two byte-exact variants in one column. A sibling actor column in the
    /// same table is null, or "system" where it is required.
    /// </summary>
    private static async Task SeedCorpusAsync(ScratchDatabase scratch)
    {
        foreach (var column in HistoricalActorNormalization.Columns)
        {
            var values = new List<string?>(EveryCase);
            values.AddRange(Enumerable.Repeat("pat.lee", column is { Table: "OperationItemRetryAttempts" } ? 20 : 3));
            if (column.Column != "AcknowledgedByUserId")
                values.Add(null);
            if (column.Table == "RuntimeControlRequests")
                values.AddRange(["PAT.LEE", "pat.lee "]);

            var siblings = HistoricalActorNormalization.Columns.Where(c => c.Table == column.Table && c != column).ToList();
            foreach (var value in values)
            {
                var row = new Dictionary<string, string?> { [column.Column] = value };
                foreach (var sibling in siblings)
                    row[sibling.Column] = sibling.Column == "AcknowledgedByUserId" ? "system" : null;
                await scratch.InsertActorRowAsync(column.Table, row);
            }
        }
    }

    // -------------------------------------------------------------------------------------------------------------
    // SQL helpers
    // -------------------------------------------------------------------------------------------------------------

    private static async Task<List<string?>> ColumnValuesAsync(string connectionString, HistoricalActorColumn column)
    {
        var values = new List<string?>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT [{column.Column}] FROM [{column.Table}];";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        return values;
    }

    private static async Task<List<string>> AllActorValuesAsync(string connectionString)
    {
        var all = new List<string>();
        foreach (var column in HistoricalActorNormalization.Columns)
            all.AddRange((await ColumnValuesAsync(connectionString, column)).Select(v => $"{column}={v ?? "<null>"}"));
        return all;
    }

    private static async Task<List<string>> IdentitySnapshotAsync(string connectionString)
    {
        var snapshot = new List<string>();
        foreach (var table in new[] { "Users", "UsernameLedger", "IdentityLinks" })
        {
            snapshot.Add(table + ":" + await ScalarAsync<long>(connectionString, $"SELECT COUNT_BIG(*) FROM [{table}];"));
            snapshot.Add(table + ":" + await ScalarAsync<int>(connectionString, $"SELECT ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM(*)), 0) FROM [{table}];"));
        }

        return snapshot;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed class ScratchDatabase(string baseConnectionString, string catalog, string connectionString) : IAsyncDisposable
    {
        private readonly Dictionary<string, List<ColumnShape>> _shapes = new(StringComparer.Ordinal);
        private int _filler;

        public string ConnectionString { get; } = connectionString;

        public List<CorpusRow> Corpus { get; } = [];

        public AppertureDbContext NewDb() =>
            new(new DbContextOptionsBuilder<AppertureDbContext>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .UseSqlServer(ConnectionString)
                .Options);

        /// <summary>Inserts a row of a normalized table and records it in the corpus the oracle checks.</summary>
        public async Task InsertActorRowAsync(string table, Dictionary<string, string?> actorValues)
        {
            await InsertRowAsync(table, actorValues.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
            Corpus.Add(new CorpusRow(table, actorValues));
        }

        /// <summary>Adds every row already in a normalized table to the corpus, so the oracle describes whole tables.</summary>
        public async Task RecordExistingRowsAsync()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            foreach (var group in HistoricalActorNormalization.Columns.GroupBy(c => c.Table))
            {
                var columns = group.Select(c => c.Column).ToList();
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {string.Join(", ", columns.Select(c => $"[{c}]"))} FROM [{group.Key}];";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = columns.Select((c, i) => (c, reader.IsDBNull(i) ? null : reader.GetString(i)))
                        .ToDictionary(x => x.c, x => x.Item2);
                    Corpus.Add(new CorpusRow(group.Key, row));
                }
            }
        }

        /// <summary>Inserts a row with the given values; every other required column without a default gets a filler.</summary>
        public async Task InsertRowAsync(string table, IReadOnlyDictionary<string, object?> values)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            var shapes = await ShapesAsync(connection, table);

            var names = new List<string>();
            await using var command = connection.CreateCommand();
            foreach (var shape in shapes)
            {
                object? value;
                if (values.TryGetValue(shape.Name, out var given))
                    value = given;
                else if (shape.Nullable || shape.Generated || shape.HasDefault)
                    continue;
                else
                    value = Filler(shape);

                var parameter = "@p" + names.Count;
                names.Add(shape.Name);
                command.Parameters.AddWithValue(parameter, value ?? DBNull.Value);
            }

            command.CommandText =
                $"INSERT INTO [{table}] ({string.Join(", ", names.Select(n => $"[{n}]"))}) VALUES ({string.Join(", ", names.Select((_, i) => "@p" + i))});";
            await command.ExecuteNonQueryAsync();
        }

        private object Filler(ColumnShape shape)
        {
            var n = Interlocked.Increment(ref _filler);
            return shape.Type switch
            {
                "nvarchar" or "varchar" or "nchar" or "char" =>
                    ("f" + n + Guid.NewGuid().ToString("N"))[..Math.Clamp(shape.MaxChars, 1, 33)],
                "int" or "bigint" => n,
                "smallint" => (short)(n % short.MaxValue),
                "tinyint" => (byte)(n % byte.MaxValue),
                "bit" => false,
                "datetimeoffset" => DateTimeOffset.UtcNow,
                "datetime2" or "datetime" or "date" => DateTime.UtcNow,
                "uniqueidentifier" => Guid.NewGuid(),
                "decimal" or "numeric" or "float" or "real" or "money" => 0m,
                "varbinary" or "binary" => new byte[] { 0 },
                _ => throw new NotSupportedException($"No filler for {shape.Type} ({shape.Name})."),
            };
        }

        private async Task<List<ColumnShape>> ShapesAsync(SqlConnection connection, string table)
        {
            if (_shapes.TryGetValue(table, out var cached))
                return cached;

            var shapes = new List<ColumnShape>();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT c.name, t.name, c.max_length, c.is_nullable,
                       CAST(CASE WHEN c.is_identity = 1 OR c.is_computed = 1 OR t.name IN (N'timestamp', N'rowversion') THEN 1 ELSE 0 END AS bit),
                       CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit)
                FROM sys.columns c
                INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE c.object_id = OBJECT_ID(@table)
                ORDER BY c.column_id;
                """;
            command.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 256) { Value = "dbo." + table });
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var type = reader.GetString(1);
                var bytes = reader.GetInt16(2);
                var chars = bytes < 0 ? 64 : type.StartsWith('n') ? bytes / 2 : bytes;
                shapes.Add(new ColumnShape(reader.GetString(0), type, chars, reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5)));
            }

            if (shapes.Count == 0)
                throw new InvalidOperationException($"Table {table} not found.");
            _shapes[table] = shapes;
            return shapes;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(
                    new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" }.ConnectionString,
                    $"""
                    IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{catalog}')
                    BEGIN
                        ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        DROP DATABASE [{catalog}];
                    END
                    """);
            }
            catch
            {
                // Cleanup is best-effort; a leftover throwaway catalog must not fail the run.
            }
        }
    }

    private sealed record ColumnShape(string Name, string Type, int MaxChars, bool Nullable, bool Generated, bool HasDefault);
}
