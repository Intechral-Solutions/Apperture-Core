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
        scratch.ExecutionMarker.Should().NotBeNullOrEmpty();
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

    private static HostConfigurationAttestationPublisher Publisher(AuditSqlScratch scratch, EntraOptionsBuildHealth health, out ServiceProvider provider)
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
            new HostAttestationIdentity("host-a", "p1", DateTimeOffset.UtcNow),
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
}
