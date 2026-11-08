using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Services;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The Management Host's attestation against real SQL Server, on both isolation modes: the database's clock stamps every time and
/// the database binding, a process owns its row and a newer process of the same host replaces it, a stop closes the window at
/// once, the freshness edge holds on values the server really returned, a copy of a row in another database is not believed, the
/// evaluation over a stored attestation gives the readings the contract says, and the publisher neither takes nor waits for the
/// administrator-set gate. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class HostConfigurationAttestationSqlServerTests
{
    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private static readonly string Tenant = "11111111-1111-1111-1111-111111111111";

    private static AttestedEntraOptions Usable() =>
        new(true, "https://login.microsoftonline.com/" + Tenant, "client", true, Tenant, "/signin-oidc", ["openid", "profile"], null, false);

    private static AttestedAuthenticationPayload Established(bool localNow = true) =>
        new(localNow, true, Usable(), new AttestedEntraNow(
            Usable(),
            EntraOptionsBuildQuality.Complete,
            new AttestedHandler(RunningHandlerState.Bound, "https://login.microsoftonline.com/" + Tenant + "/v2.0", "client", true, EntraOptionsBuildQuality.Complete)));

    private static HostConfigurationAttestationWrite Write(string process, AttestedAuthenticationPayload? payload = null, string host = "host-a") =>
        new(host, process, new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero), AttestedAuthenticationPayload.SchemaVersion, (payload ?? Established()).Serialize());

    /// <summary>A context with no retrying execution strategy, so the test can open its own transaction as a unit does.</summary>
    private static AppertureDbContext PlainDb(AuditSqlScratch scratch) =>
        new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(scratch.ConnectionString)
            .Options);

    private static SqlHostConfigurationAttestationStore Store(AuditSqlScratch scratch, out AppertureDbContext db)
    {
        db = scratch.NewDb();
        return new SqlHostConfigurationAttestationStore(db);
    }

    // ---------------------------------------------------------------- the database's clock and binding

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Every_time_is_the_databases_and_the_binding_is_computed_on_the_server(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;

        var before = await scratch.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME();");
        await store.ClaimAsync(Write("p1"));
        var after = await scratch.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME();");
        var snapshot = await store.ReadAsync();

        var row = snapshot.Rows.Should().ContainSingle().Subject;
        row.ClaimedUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after, "the claim is stamped by the database, not by the host's 1999 clock");
        row.RefreshedUtc.Should().Be(row.ClaimedUtc);
        row.StoppedUtc.Should().BeNull();
        row.ProcessStartedUtc.Year.Should().Be(1999, "the host's own start time is kept, informational only");
        row.DatabaseFingerprint.Should().MatchRegex("^[0-9A-F]{64}$").And.Be(snapshot.DatabaseFingerprint);
        snapshot.DatabaseNowUtc.Should().BeOnOrAfter(row.RefreshedUtc);
        row.Source.Should().Be("management_host");
        row.Payload.Should().Be(Write("p1").Payload);

        // Execution proof: state read back with SQL this test sends itself, on its own connection, from the catalog it created.
        // The store under test is not involved, so a store that returned canned rows, or a run that never reached SQL Server, fails here.
        (await scratch.ScalarAsync<string>("SELECT DB_NAME();")).Should().Be(scratch.Catalog);
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.HostConfigurationAttestations;")).Should().Be(1);
        (await scratch.ScalarAsync<string>("SELECT HostId FROM dbo.HostConfigurationAttestations;")).Should().Be(row.HostId);
        (await scratch.ScalarAsync<string>("SELECT Payload FROM dbo.HostConfigurationAttestations;")).Should().Be(row.Payload);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refresh_moves_only_the_refresh_time_and_the_reading(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1", Established(localNow: true)));
        var claimed = (await store.ReadAsync()).Rows.Single();

        (await store.RefreshAsync(Write("p1", Established(localNow: false)))).Should().Be(HostAttestationRefreshOutcome.Refreshed);

        var refreshed = (await store.ReadAsync()).Rows.Single();
        refreshed.ClaimedUtc.Should().Be(claimed.ClaimedUtc);
        refreshed.RefreshedUtc.Should().BeAfter(claimed.RefreshedUtc);
        refreshed.HostId.Should().Be(claimed.HostId);
        refreshed.ProcessInstanceId.Should().Be(claimed.ProcessInstanceId, "a refresh keeps the owner");
        refreshed.ProcessStartedUtc.Should().Be(claimed.ProcessStartedUtc);
        refreshed.StoppedUtc.Should().BeNull();
        refreshed.DatabaseFingerprint.Should().Be(claimed.DatabaseFingerprint, "the binding of a row that never moved is unchanged");
        HostConfigurationAttestationClassifier.Assess(await store.ReadAsync(), HostConfigurationAttestationTiming.FreshnessWindow).IsAttested.Should().BeTrue();
        AttestedAuthenticationPayload.TryParse(refreshed.Payload, out var payload).Should().BeTrue();
        payload!.LocalNow.Should().BeFalse("a reading changed on the host is what the next refresh attests");
    }

    // ---------------------------------------------------------------- who owns the row

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_newer_process_replaces_the_row_and_the_older_one_can_neither_refresh_nor_stop_it(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("old"));
        await store.ClaimAsync(Write("new", Established(localNow: false)));

        (await store.RefreshAsync(Write("old"))).Should().Be(HostAttestationRefreshOutcome.Superseded);
        (await store.MarkStoppedAsync("host-a", "old")).Should().BeFalse("an older process cannot close the newer process's window");

        var row = (await store.ReadAsync()).Rows.Single();
        row.ProcessInstanceId.Should().Be("new");
        row.StoppedUtc.Should().BeNull();
        AttestedAuthenticationPayload.TryParse(row.Payload, out var payload).Should().BeTrue();
        payload!.LocalNow.Should().BeFalse("the old process did not overwrite the new reading");
        (await store.RefreshAsync(Write("new"))).Should().Be(HostAttestationRefreshOutcome.Refreshed);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_graceful_stop_closes_the_window_and_the_stopped_process_cannot_refresh(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1"));

        (await store.MarkStoppedAsync("host-a", "p1")).Should().BeTrue();
        (await store.MarkStoppedAsync("host-a", "p1")).Should().BeFalse("it is already stopped");
        (await store.RefreshAsync(Write("p1"))).Should().Be(HostAttestationRefreshOutcome.Superseded, "a stopped process publishes nothing more");

        var snapshot = await store.ReadAsync();
        snapshot.Rows.Single().StoppedUtc.Should().NotBeNull();
        HostConfigurationAttestationClassifier.Assess(snapshot, HostConfigurationAttestationTiming.FreshnessWindow).Kind
            .Should().Be(AttestationAssessmentKind.Stopped, "a row refreshed a moment ago is not evidence once it is stopped");

        await store.ClaimAsync(Write("p2"));
        HostConfigurationAttestationClassifier.Assess(await store.ReadAsync(), HostConfigurationAttestationTiming.FreshnessWindow).IsAttested
            .Should().BeTrue("a restart claims the row again and clears the mark");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_host_with_no_row_is_told_so_and_claims_it_again(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;

        (await store.RefreshAsync(Write("p1"))).Should().Be(HostAttestationRefreshOutcome.Missing);
        HostConfigurationAttestationClassifier.Assess(await store.ReadAsync(), HostConfigurationAttestationTiming.FreshnessWindow).Kind
            .Should().Be(AttestationAssessmentKind.Missing);

        await store.ClaimAsync(Write("p1"));
        (await store.RefreshAsync(Write("p1"))).Should().Be(HostAttestationRefreshOutcome.Refreshed);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Concurrent_claims_of_one_host_leave_exactly_one_row_owned_by_exactly_one_process(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var processes = Enumerable.Range(0, 8).Select(i => "p" + i).ToList();

        var claims = processes.Select(async process =>
        {
            await using var db = scratch.NewDb();
            await new SqlHostConfigurationAttestationStore(db).ClaimAsync(Write(process));
        });
        await Task.WhenAll(claims);

        await using var reader = scratch.NewDb();
        var store = new SqlHostConfigurationAttestationStore(reader);
        var row = (await store.ReadAsync()).Rows.Should().ContainSingle().Subject;
        processes.Should().Contain(row.ProcessInstanceId);
        (await store.RefreshAsync(Write(row.ProcessInstanceId))).Should().Be(HostAttestationRefreshOutcome.Refreshed);
        foreach (var other in processes.Where(p => p != row.ProcessInstanceId))
            (await store.RefreshAsync(Write(other))).Should().Be(HostAttestationRefreshOutcome.Superseded);
    }

    // ---------------------------------------------------------------- freshness on values the server returned

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_freshness_edge_holds_on_the_values_the_database_really_returned(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1"));
        // Age the row by a known amount of the database's own time.
        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -170, SYSUTCDATETIME());");

        var snapshot = await store.ReadAsync();
        var age = snapshot.DatabaseNowUtc - snapshot.Rows.Single().RefreshedUtc;

        age.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(170));
        HostConfigurationAttestationClassifier.Assess(snapshot, age).IsAttested.Should().BeTrue("exactly the window is still fresh");
        HostConfigurationAttestationClassifier.Assess(snapshot, age - TimeSpan.FromTicks(1)).Kind.Should().Be(AttestationAssessmentKind.Stale, "one tick more is stale");
        HostConfigurationAttestationClassifier.Assess(snapshot, HostConfigurationAttestationTiming.FreshnessWindow).IsAttested
            .Should().BeTrue("170 seconds old is inside the real 180-second window");

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -181, SYSUTCDATETIME());");
        HostConfigurationAttestationClassifier.Assess(await store.ReadAsync(), HostConfigurationAttestationTiming.FreshnessWindow).Kind
            .Should().Be(AttestationAssessmentKind.Stale);

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, 600, SYSUTCDATETIME());");
        HostConfigurationAttestationClassifier.Assess(await store.ReadAsync(), HostConfigurationAttestationTiming.FreshnessWindow).Kind
            .Should().Be(AttestationAssessmentKind.ClockInconsistent, "a row dated after the database's own clock is never believed");
    }

    // ---------------------------------------------------------------- the database it was written for

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_row_copied_into_another_database_is_not_believed_there(bool rcsi)
    {
        await using var source = await AuditIsolation.CreateAsync("Apperture_AttestA_", rcsi);
        await using var target = await AuditIsolation.CreateAsync("Apperture_AttestB_", rcsi);
        var sourceStore = Store(source, out var sourceDb);
        var targetStore = Store(target, out var targetDb);
        await using var _ = sourceDb;
        await using var __ = targetDb;
        await sourceStore.ClaimAsync(Write("p1"));
        var row = (await sourceStore.ReadAsync()).Rows.Single();

        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(target.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO HostConfigurationAttestations (HostId, Source, SchemaVersion, ProcessInstanceId, ProcessStartedUtc, ClaimedUtc, RefreshedUtc, StoppedUtc, DatabaseFingerprint, Payload) " +
                "VALUES (@h, @s, @v, @p, @st, SYSUTCDATETIME(), SYSUTCDATETIME(), NULL, @f, @pl);";
            insert.Parameters.AddWithValue("@h", row.HostId);
            insert.Parameters.AddWithValue("@s", row.Source);
            insert.Parameters.AddWithValue("@v", row.SchemaVersion);
            insert.Parameters.AddWithValue("@p", row.ProcessInstanceId);
            insert.Parameters.AddWithValue("@st", row.ProcessStartedUtc);
            insert.Parameters.AddWithValue("@f", row.DatabaseFingerprint);
            insert.Parameters.AddWithValue("@pl", row.Payload);
            await insert.ExecuteNonQueryAsync();
        }

        HostConfigurationAttestationClassifier.Assess(await sourceStore.ReadAsync(), HostConfigurationAttestationTiming.FreshnessWindow).IsAttested.Should().BeTrue();
        var copied = await targetStore.ReadAsync();
        copied.DatabaseFingerprint.Should().NotBe(row.DatabaseFingerprint, "the binding names the server and the database");
        HostConfigurationAttestationClassifier.Assess(copied, HostConfigurationAttestationTiming.FreshnessWindow).Kind
            .Should().Be(AttestationAssessmentKind.DatabaseMismatch);
    }

    // ---------------------------------------------------------------- inside the caller's transaction

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_store_joins_an_open_transaction_so_a_reader_inside_a_unit_reads_under_the_units_isolation(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        await using var db = PlainDb(scratch);
        var store = new SqlHostConfigurationAttestationStore(db);

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await new SqlPlatformAdministratorSetGate(db).AcquireAsync();
            await store.ClaimAsync(Write("p1"));
            (await store.ReadAsync()).Rows.Should().ContainSingle("the transaction reads its own claim");
            await transaction.RollbackAsync();
        }

        (await store.ReadAsync()).Rows.Should().BeEmpty("the claim was part of the rolled-back transaction");
    }

    // ---------------------------------------------------------------- the evaluation over a stored attestation

    private static EffectiveSignInConfigurationProvider Provider(AuditSqlScratch scratch, out AppertureDbContext db)
    {
        db = scratch.NewDb();
        return new EffectiveSignInConfigurationProvider(
            new AttestedDeploymentAuthenticationConfiguration(new SqlHostConfigurationAttestationStore(db)),
            new SqlEntraInstanceRegistrationRepository(db));
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_provider_reads_what_the_host_attested_and_nothing_from_the_command_lines_own_configuration(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var provider = Provider(scratch, out var db);
        await using var _ = db;
        await new SqlHostConfigurationAttestationStore(db).ClaimAsync(Write("p1", Established(localNow: false)));

        var configuration = await provider.ReadAsync();

        configuration.Trust.Local.Should().Be(ConfigurationTrustLevel.Trusted);
        configuration.Trust.Evidence.Should().Be(ConfigurationEvidence.Attested);
        configuration.Now.LocalSignInEnabled.Should().BeFalse("the host's running options said Local was off");
        configuration.Restart.LocalSignInEnabled.Should().BeTrue("the host's configuration says Local is on for the next start");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Each_way_the_evidence_can_fail_makes_both_inputs_unknown_through_the_real_provider(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var provider = Provider(scratch, out var db);
        await using var _ = db;
        var store = new SqlHostConfigurationAttestationStore(db);

        async Task AssertUnavailableAsync(string because)
        {
            var configuration = await provider.ReadAsync();
            configuration.Trust.Local.Should().Be(ConfigurationTrustLevel.Unknown, because);
            configuration.Trust.Entra.Should().Be(ConfigurationTrustLevel.Unknown, because);
            configuration.Trust.Evidence.Should().Be(ConfigurationEvidence.Unavailable, because);
            configuration.Trust.Reasons.Should().NotBeEmpty(because);
        }

        await AssertUnavailableAsync("no host has recorded anything");

        await store.ClaimAsync(Write("p1"));
        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -200, SYSUTCDATETIME());");
        await AssertUnavailableAsync("the record is older than the window");

        await store.ClaimAsync(Write("p1"));
        await store.MarkStoppedAsync("host-a", "p1");
        await AssertUnavailableAsync("the host stopped");

        await store.ClaimAsync(Write("p1"));
        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET Payload = N'{';");
        await AssertUnavailableAsync("the record cannot be read");

        await store.ClaimAsync(Write("p1"));
        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET SchemaVersion = 2;");
        await AssertUnavailableAsync("the record is of a version this reader does not know");

        await store.ClaimAsync(Write("p1"));
        await store.ClaimAsync(Write("p2", host: "host-b"));
        await AssertUnavailableAsync("two hosts have recorded recently");

        await scratch.ExecuteAsync("DELETE FROM HostConfigurationAttestations WHERE HostId = N'host-b';");
        (await provider.ReadAsync()).Trust.Evidence.Should().Be(ConfigurationEvidence.Attested, "one live host is evidence again");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_change_on_the_host_is_visible_only_after_its_next_refresh_and_that_is_the_stated_bound(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var provider = Provider(scratch, out var db);
        await using var _ = db;
        var store = new SqlHostConfigurationAttestationStore(db);
        await store.ClaimAsync(Write("p1", Established(localNow: true)));

        // The host reloads its configuration and turns Local off. Until its next refresh the record still says on, inside the window.
        (await provider.ReadAsync()).Now.LocalSignInEnabled.Should().BeTrue("the window bounds the staleness; it does not remove it");

        await store.RefreshAsync(Write("p1", Established(localNow: false)));
        (await provider.ReadAsync()).Now.LocalSignInEnabled.Should().BeFalse();
    }

    // ---------------------------------------------------------------- the publisher against real SQL

    private static HostConfigurationAttestationPublisher Publisher(AuditSqlScratch scratch, EntraOptionsBuildHealth health, out ServiceProvider provider, string process = "p1")
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => scratch.NewDb());
        services.AddScoped<IHostConfigurationAttestationStore, SqlHostConfigurationAttestationStore>();
        provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LocalAuth:Enabled"] = "true" })
            .Build();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        return new HostConfigurationAttestationPublisher(
            scopes,
            new HostAttestationPayloadSource(config, scopes, health),
            new HostAttestationIdentity("host-a", process, DateTimeOffset.UtcNow),
            TimeProvider.System,
            NullLogger<HostConfigurationAttestationPublisher>.Instance);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_publisher_writes_its_row_and_marks_it_stopped_without_ever_taking_or_waiting_for_the_gate(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var publisher = Publisher(scratch, new EntraOptionsBuildHealth(), out var provider);
        await using var _ = provider;
        await using var holder = await HoldTheGateAsync(scratch);

        // Another change to administrator access holds the gate for the whole test. The attestation must neither wait for it nor
        // be waited for: otherwise a refresh could queue behind a change that is itself reading the attestation.
        (await publisher.PublishOnceAsync()).Should().BeTrue();
        (await publisher.PublishOnceAsync()).Should().BeTrue();
        await publisher.StopAsync(CancellationToken.None);

        (await ApplicationLockWaitersAsync(scratch)).Should().Be(0);
        await using var db = scratch.NewDb();
        var snapshot = await new SqlHostConfigurationAttestationStore(db).ReadAsync();
        var row = snapshot.Rows.Single();
        row.StoppedUtc.Should().NotBeNull();
        AttestedAuthenticationPayload.TryParse(row.Payload, out var payload).Should().BeTrue();
        payload!.EntraNow.Should().BeNull("nothing had built the options");
        payload.LocalNow.Should().BeTrue();
        payload.LocalRestart.Should().BeTrue();
        (await ProbeGateAsync(scratch)).Should().Be(-1, "the other change still holds the gate: the publisher did not touch it");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_change_that_holds_the_gate_reads_the_attestation_while_the_host_refreshes_it(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var publisher = Publisher(scratch, new EntraOptionsBuildHealth(), out var provider);
        await using var _ = provider;
        await publisher.PublishOnceAsync();

        await using var unitDb = PlainDb(scratch);
        await using var transaction = await unitDb.Database.BeginTransactionAsync();
        await new SqlPlatformAdministratorSetGate(unitDb).AcquireAsync();

        var reading = await new AttestedDeploymentAuthenticationConfiguration(new SqlHostConfigurationAttestationStore(unitDb)).ReadAsync();
        reading.Evidence.Should().Be(ConfigurationEvidence.Attested);

        // The host's refresh runs while the unit holds the gate and its transaction is open: it completes and does not wait for the unit.
        (await publisher.PublishOnceAsync().WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
        await transaction.CommitAsync();
    }

    // ---------------------------------------------------------------- taking a row back (a superseded or silent process)

    private static readonly TimeSpan Window = HostConfigurationAttestationTiming.FreshnessWindow;

    private static AttestationAssessment Assess(HostConfigurationAttestationSnapshot snapshot) =>
        HostConfigurationAttestationClassifier.Assess(snapshot, Window);

    /// <summary>The state of the single row, read with SQL the test sends itself, so the store under test is not its own witness.</summary>
    private static async Task<(string Process, bool Stopped, string Fingerprint)> RawAsync(AuditSqlScratch scratch) =>
        (await scratch.ScalarAsync<string>("SELECT ProcessInstanceId FROM dbo.HostConfigurationAttestations;"),
         await scratch.ScalarAsync<int>("SELECT CASE WHEN StoppedUtc IS NULL THEN 0 ELSE 1 END FROM dbo.HostConfigurationAttestations;") == 1,
         await scratch.ScalarAsync<string>("SELECT DatabaseFingerprint FROM dbo.HostConfigurationAttestations;"));

    /// <summary>Every candidate, on its own context and connection, reclaims after a common start signal, so the statements overlap on the server.</summary>
    private static async Task<List<(string Candidate, HostAttestationReclaimOutcome Outcome)>> RaceReclaimsAsync(AuditSqlScratch scratch, IReadOnlyList<string> candidates)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        var tasks = candidates.Select(async candidate =>
        {
            await using var db = scratch.NewDb();
            await db.Database.OpenConnectionAsync();   // connect first, so only the statements race
            Interlocked.Increment(ref ready);
            await start.Task;
            return (candidate, await new SqlHostConfigurationAttestationStore(db).ReclaimAsync(Write(candidate)));
        }).ToList();

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (Volatile.Read(ref ready) < candidates.Count && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        start.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_live_holder_is_not_displaced_by_a_reclaim_and_nothing_about_its_row_changes(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("old"));
        await store.ClaimAsync(Write("new", Established(localNow: false)));
        var before = (await store.ReadAsync()).Rows.Single();

        (await store.ReclaimAsync(Write("old"))).Should().Be(HostAttestationReclaimOutcome.Held, "the process that superseded it is live");

        var after = (await store.ReadAsync()).Rows.Single();
        after.Should().Be(before, "a refused reclaim writes nothing, not even the refresh time");
        (await RawAsync(scratch)).Process.Should().Be("new");
        (await store.RefreshAsync(Write("new"))).Should().Be(HostAttestationRefreshOutcome.Refreshed);
        (await store.RefreshAsync(Write("old"))).Should().Be(HostAttestationRefreshOutcome.Superseded);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_holder_that_stopped_is_superseded_by_a_reclaim_and_the_old_process_owns_the_row_again(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("old"));
        await store.ClaimAsync(Write("new"));
        (await store.MarkStoppedAsync("host-a", "new")).Should().BeTrue();
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Stopped);

        (await store.ReclaimAsync(Write("old", Established(localNow: false)))).Should().Be(HostAttestationReclaimOutcome.Reclaimed);

        var snapshot = await store.ReadAsync();
        var row = snapshot.Rows.Should().ContainSingle().Subject;
        row.ProcessInstanceId.Should().Be("old");
        row.StoppedUtc.Should().BeNull("the mark of the process that stopped is cleared by the new owner");
        row.DatabaseFingerprint.Should().Be(snapshot.DatabaseFingerprint);
        Assess(snapshot).IsAttested.Should().BeTrue();
        AttestedAuthenticationPayload.TryParse(row.Payload, out var payload).Should().BeTrue();
        payload!.LocalNow.Should().BeFalse("the row carries the reading of the process that took it, not the stopped one's");
        (await store.RefreshAsync(Write("old"))).Should().Be(HostAttestationRefreshOutcome.Refreshed);
        (await store.RefreshAsync(Write("new"))).Should().Be(HostAttestationRefreshOutcome.Superseded);
        (await RawAsync(scratch)).Should().Be(("old", false, snapshot.DatabaseFingerprint));
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_holder_that_went_quiet_longer_than_the_window_is_superseded_and_one_inside_it_or_dated_ahead_is_not(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("old"));
        await store.ClaimAsync(Write("new"));

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -170, SYSUTCDATETIME());");
        (await store.ReclaimAsync(Write("old"))).Should().Be(HostAttestationReclaimOutcome.Held, "170 seconds is inside the 180-second window: the holder is live");
        Assess(await store.ReadAsync()).IsAttested.Should().BeTrue();

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, 600, SYSUTCDATETIME());");
        (await store.ReclaimAsync(Write("old"))).Should().Be(HostAttestationReclaimOutcome.Held, "a record dated after the database's clock cannot be judged, so it is held");
        (await RawAsync(scratch)).Process.Should().Be("new");

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -181, SYSUTCDATETIME());");
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Stale);
        (await store.ReclaimAsync(Write("old"))).Should().Be(HostAttestationReclaimOutcome.Reclaimed, "the reader already calls 181 seconds stale; the store agrees");

        var snapshot = await store.ReadAsync();
        snapshot.Rows.Single().ProcessInstanceId.Should().Be("old");
        Assess(snapshot).IsAttested.Should().BeTrue("a reclaim stamps a new database time: the row is live from this moment");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_holder_that_went_quiet_for_most_of_the_window_but_not_all_of_it_is_still_held_and_never_displaced(bool rcsi)
    {
        // The window is 180 seconds. The age is set by the database's own clock and read back from it, so each case is proven to sit
        // where the name says before the reclaim is asked. 176 is just past a 175-second bound; 178 and 179 sit inside the last
        // seconds, where a store that called its holder silent a few seconds before the reader does would take a live row.
        //
        // The row keeps ageing while the test runs, so a stall of the test database (a pause of a second or two under memory pressure)
        // could make a correct store see a holder that really is past the window. A stopwatch bounds the real time from the update to
        // the end of the reclaim: when age + that time could have reached the window the attempt proves nothing either way and is
        // repeated, and otherwise the holder is certainly live and must be Held. A store that is wrong is never excused: its mistake
        // shows at 176 seconds with 4 seconds of margin.
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("old"));
        await store.ClaimAsync(Write("new"));

        foreach (var seconds in new[] { 176, 178, 179 })
        {
            var conclusive = false;
            for (var attempt = 1; attempt <= 3 && !conclusive; attempt++)
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                await scratch.ExecuteAsync($"UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -{seconds}, SYSUTCDATETIME());");

                var before = await store.ReadAsync();
                var age = before.DatabaseNowUtc - before.Rows.Single().RefreshedUtc;
                age.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(seconds), $"the row was set {seconds} seconds back by the database's clock");
                if (age >= HostConfigurationAttestationTiming.FreshnessWindow)
                    continue;

                Assess(before).IsAttested.Should().BeTrue($"the reader still believes a holder {seconds} seconds old");

                var outcome = await store.ReclaimAsync(Write("old"));
                elapsed.Stop();
                if (TimeSpan.FromSeconds(seconds) + elapsed.Elapsed >= HostConfigurationAttestationTiming.FreshnessWindow)
                    continue;

                conclusive = true;
                outcome.Should().Be(
                    HostAttestationReclaimOutcome.Held,
                    $"a holder {seconds} seconds old is live by the reader's rule, so the store must not displace it");
                (await RawAsync(scratch)).Process.Should().Be("new", "the live holder keeps its row");
                (await store.ReadAsync()).Rows.Single().Should().Be(before.Rows.Single(), "a refused reclaim writes nothing, not even the refresh time");
            }

            conclusive.Should().BeTrue($"the {seconds}-second case was repeated three times and each time the test database was too slow to prove anything");
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_holder_that_went_quiet_just_longer_than_the_window_is_reclaimed_so_the_store_is_not_laxer_than_the_reader(bool rcsi)
    {
        // The other side of the edge: 180.5 seconds is stale by the reader's rule, and a store that waited a second longer (a 181-second
        // bound) would hold it. The row only ever gets older while the test runs, so a slow database cannot make this fail.
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("old"));
        await store.ClaimAsync(Write("new"));

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(MILLISECOND, -180500, SYSUTCDATETIME());");
        var before = await store.ReadAsync();
        (before.DatabaseNowUtc - before.Rows.Single().RefreshedUtc).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180_500));
        Assess(before).Kind.Should().Be(AttestationAssessmentKind.Stale, "the reader already calls a holder this quiet stale");

        (await store.ReclaimAsync(Write("old"))).Should().Be(HostAttestationReclaimOutcome.Reclaimed, "the store agrees with the reader as soon as the window has passed");
        (await RawAsync(scratch)).Process.Should().Be("old");
        Assess(await store.ReadAsync()).IsAttested.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reclaim_by_the_owner_is_idempotent_and_a_stopped_process_cannot_take_its_row_back(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1"));
        var claimed = (await store.ReadAsync()).Rows.Single();

        // A reclaim whose acknowledgement was lost and is sent again.
        (await store.ReclaimAsync(Write("p1"))).Should().Be(HostAttestationReclaimOutcome.Reclaimed);
        var again = (await store.ReadAsync()).Rows.Single();
        again.ClaimedUtc.Should().Be(claimed.ClaimedUtc, "the owner's claim time is not rewritten");
        again.ProcessStartedUtc.Should().Be(claimed.ProcessStartedUtc);
        again.ProcessInstanceId.Should().Be("p1");
        again.RefreshedUtc.Should().BeOnOrAfter(claimed.RefreshedUtc);

        (await store.MarkStoppedAsync("host-a", "p1")).Should().BeTrue();
        (await store.ReclaimAsync(Write("p1"))).Should().Be(HostAttestationReclaimOutcome.Held, "a process that stopped publishes nothing more, as a refresh already says");
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Stopped);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reclaim_with_no_row_creates_it_and_leaves_another_hosts_row_alone(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("other", host: "host-b"));

        (await store.ReclaimAsync(Write("p1"))).Should().Be(HostAttestationReclaimOutcome.Reclaimed);

        var snapshot = await store.ReadAsync();
        snapshot.Rows.Should().HaveCount(2);
        snapshot.Rows.Single(r => r.HostId == "host-a").ProcessInstanceId.Should().Be("p1");
        snapshot.Rows.Single(r => r.HostId == "host-a").DatabaseFingerprint.Should().Be(snapshot.DatabaseFingerprint);
        snapshot.Rows.Single(r => r.HostId == "host-b").ProcessInstanceId.Should().Be("other");
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.HostConfigurationAttestations;")).Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Of_many_candidates_for_a_silent_holders_row_exactly_one_obtains_it(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        await using var seedDb = scratch.NewDb();
        var seed = new SqlHostConfigurationAttestationStore(seedDb);

        for (var round = 0; round < 6; round++)
        {
            // Alternate the two ways a holder can stop being live, so both branches of the statement are raced.
            await seed.ClaimAsync(Write("holder" + round));
            await scratch.ExecuteAsync(round % 2 == 0
                ? "UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -200, SYSUTCDATETIME());"
                : "UPDATE HostConfigurationAttestations SET StoppedUtc = SYSUTCDATETIME();");
            var candidates = Enumerable.Range(0, 8).Select(i => $"r{round}c{i}").ToList();

            var outcomes = await RaceReclaimsAsync(scratch, candidates);

            var winners = outcomes.Where(o => o.Outcome == HostAttestationReclaimOutcome.Reclaimed).Select(o => o.Candidate).ToList();
            winners.Should().ContainSingle($"round {round}: two candidates must never both obtain the row");
            var raw = await RawAsync(scratch);
            raw.Process.Should().Be(winners[0]);
            raw.Stopped.Should().BeFalse();
            (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.HostConfigurationAttestations;")).Should().Be(1);

            await using var reader = scratch.NewDb();
            var store = new SqlHostConfigurationAttestationStore(reader);
            Assess(await store.ReadAsync()).IsAttested.Should().BeTrue();
            (await store.RefreshAsync(Write(winners[0]))).Should().Be(HostAttestationRefreshOutcome.Refreshed);
            foreach (var loser in candidates.Where(c => c != winners[0]))
                (await store.RefreshAsync(Write(loser))).Should().Be(HostAttestationRefreshOutcome.Superseded);
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Of_many_candidates_for_an_absent_row_exactly_one_obtains_it(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var candidates = Enumerable.Range(0, 8).Select(i => "c" + i).ToList();

        var outcomes = await RaceReclaimsAsync(scratch, candidates);

        var winners = outcomes.Where(o => o.Outcome == HostAttestationReclaimOutcome.Reclaimed).Select(o => o.Candidate).ToList();
        winners.Should().ContainSingle();
        (await scratch.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.HostConfigurationAttestations;")).Should().Be(1);
        (await RawAsync(scratch)).Process.Should().Be(winners[0]);
    }

    // ---------------------------------------------------------------- the database binding

    private const string WrongBinding = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refresh_by_the_owner_replaces_an_outdated_binding_so_the_record_is_believed_again(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1"));
        // The database was renamed, or the host's connection now reaches another server: the stored binding is no longer this database's.
        await scratch.ExecuteAsync($"UPDATE HostConfigurationAttestations SET DatabaseFingerprint = N'{WrongBinding}';");
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.DatabaseMismatch);

        (await store.RefreshAsync(Write("p1"))).Should().Be(HostAttestationRefreshOutcome.Refreshed);

        var snapshot = await store.ReadAsync();
        snapshot.Rows.Single().DatabaseFingerprint.Should().Be(snapshot.DatabaseFingerprint).And.NotBe(WrongBinding);
        Assess(snapshot).IsAttested.Should().BeTrue();
        (await RawAsync(scratch)).Fingerprint.Should().Be(snapshot.DatabaseFingerprint, "read back with SQL of the test's own");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refresh_by_a_process_that_does_not_own_the_row_cannot_rebind_it_or_take_it(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1"));
        await scratch.ExecuteAsync($"UPDATE HostConfigurationAttestations SET DatabaseFingerprint = N'{WrongBinding}';");

        (await store.RefreshAsync(Write("p2"))).Should().Be(HostAttestationRefreshOutcome.Superseded);
        (await RawAsync(scratch)).Should().Be(("p1", false, WrongBinding), "a process that is not the owner does not touch the row, so it cannot vouch for it");

        (await store.MarkStoppedAsync("host-a", "p1")).Should().BeTrue();
        (await store.RefreshAsync(Write("p1"))).Should().Be(HostAttestationRefreshOutcome.Superseded);
        (await RawAsync(scratch)).Fingerprint.Should().Be(WrongBinding, "a stopped owner cannot rebind its row either");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_live_record_for_another_database_is_refused_and_a_silent_one_is_replaced_with_this_databases_binding(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1"));
        await scratch.ExecuteAsync($"UPDATE HostConfigurationAttestations SET DatabaseFingerprint = N'{WrongBinding}';");

        // A live row written for another database is not displaced by a candidate, and is not believed by the reader either.
        (await store.ReclaimAsync(Write("p2"))).Should().Be(HostAttestationReclaimOutcome.Held);
        (await RawAsync(scratch)).Should().Be(("p1", false, WrongBinding));
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.DatabaseMismatch);

        // Once it has gone silent it is nobody's evidence, and the candidate's reclaim binds the row to this database.
        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -181, SYSUTCDATETIME());");
        (await store.ReclaimAsync(Write("p2"))).Should().Be(HostAttestationReclaimOutcome.Reclaimed);
        var snapshot = await store.ReadAsync();
        snapshot.Rows.Single().DatabaseFingerprint.Should().Be(snapshot.DatabaseFingerprint);
        Assess(snapshot).IsAttested.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Conflicting_hosts_stay_a_conflict_and_a_reclaim_never_resolves_it_for_another_host(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var store = Store(scratch, out var db);
        await using var _ = db;
        await store.ClaimAsync(Write("p1"));
        await store.ClaimAsync(Write("q1", host: "host-b"));
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Conflict);

        (await store.ReclaimAsync(Write("p2"))).Should().Be(HostAttestationReclaimOutcome.Held);
        (await store.ReclaimAsync(Write("p1"))).Should().Be(HostAttestationReclaimOutcome.Reclaimed, "its own live row, idempotently");

        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Conflict, "the other host's live row is untouched");
        (await store.ReadAsync()).Rows.Single(r => r.HostId == "host-b").ProcessInstanceId.Should().Be("q1");
    }

    // ---------------------------------------------------------------- the publisher against real SQL: interruption and recovery

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_superseded_publisher_takes_the_row_back_when_the_holder_stops_or_goes_quiet_and_never_while_it_is_live(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var older = Publisher(scratch, new EntraOptionsBuildHealth(), out var olderProvider, "older");
        var newer = Publisher(scratch, new EntraOptionsBuildHealth(), out var newerProvider, "newer");
        await using var _ = olderProvider;
        await using var __ = newerProvider;
        await using var db = scratch.NewDb();
        var store = new SqlHostConfigurationAttestationStore(db);

        (await older.PublishOnceAsync()).Should().BeTrue();
        (await newer.PublishOnceAsync()).Should().BeTrue("a process that starts replaces the one it succeeds");

        // The newer process is live: the older one asks on every tick and never gets the row.
        for (var tick = 0; tick < 3; tick++)
        {
            (await older.PublishOnceAsync()).Should().BeFalse();
            (await newer.PublishOnceAsync()).Should().BeTrue();
        }

        older.Superseded.Should().BeTrue();
        (await RawAsync(scratch)).Process.Should().Be("newer");
        Assess(await store.ReadAsync()).IsAttested.Should().BeTrue();

        // The newer process stops gracefully: the older one has the row at its next tick, and the record is evidence again.
        await newer.StopAsync(CancellationToken.None);
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Stopped);
        (await older.PublishOnceAsync()).Should().BeTrue();
        older.Superseded.Should().BeFalse();
        (await RawAsync(scratch)).Should().Be(("older", false, (await store.ReadAsync()).DatabaseFingerprint));
        Assess(await store.ReadAsync()).IsAttested.Should().BeTrue();
        (await newer.PublishOnceAsync()).Should().BeFalse("a stopped process publishes nothing more");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_publisher_whose_holder_crashed_takes_the_row_back_only_after_the_window_and_the_gap_is_never_evidence(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var survivor = Publisher(scratch, new EntraOptionsBuildHealth(), out var survivorProvider, "survivor");
        var crashing = Publisher(scratch, new EntraOptionsBuildHealth(), out var crashingProvider, "crashing");
        await using var _ = survivorProvider;
        await using var __ = crashingProvider;
        await using var db = scratch.NewDb();
        var store = new SqlHostConfigurationAttestationStore(db);
        await survivor.PublishOnceAsync();
        await crashing.PublishOnceAsync();   // the second process claims, then dies: no refresh, no stop

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -100, SYSUTCDATETIME());");
        (await survivor.PublishOnceAsync()).Should().BeFalse("100 seconds of silence is inside the window: the crashed process might still be alive");
        (await RawAsync(scratch)).Process.Should().Be("crashing");

        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -200, SYSUTCDATETIME());");
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Stale, "nobody has refreshed: the gap is not evidence");

        (await survivor.PublishOnceAsync()).Should().BeTrue();
        survivor.Superseded.Should().BeFalse();
        Assess(await store.ReadAsync()).IsAttested.Should().BeTrue();
        (await RawAsync(scratch)).Process.Should().Be("survivor");
        (await crashing.PublishOnceAsync()).Should().BeFalse("the process that came back from the dead finds the row held by a live process");
        crashing.Superseded.Should().BeTrue();
        (await RawAsync(scratch)).Process.Should().Be("survivor");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_publisher_that_missed_its_refreshes_is_not_evidence_until_it_writes_again_and_a_stopped_one_never_is(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Attest_", rcsi);
        var publisher = Publisher(scratch, new EntraOptionsBuildHealth(), out var provider);
        await using var _ = provider;
        await using var db = scratch.NewDb();
        var store = new SqlHostConfigurationAttestationStore(db);
        await publisher.PublishOnceAsync();

        // The process was suspended, or the database unreachable, for longer than the window.
        await scratch.ExecuteAsync("UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -400, SYSUTCDATETIME());");
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Stale);

        (await publisher.PublishOnceAsync()).Should().BeTrue();
        Assess(await store.ReadAsync()).IsAttested.Should().BeTrue("a fresh write by the owner, of a reading made now, is evidence again");

        await publisher.StopAsync(CancellationToken.None);
        (await publisher.PublishOnceAsync()).Should().BeFalse();
        Assess(await store.ReadAsync()).Kind.Should().Be(AttestationAssessmentKind.Stopped, "stopping closes the record and nothing reopens it");
    }
}
