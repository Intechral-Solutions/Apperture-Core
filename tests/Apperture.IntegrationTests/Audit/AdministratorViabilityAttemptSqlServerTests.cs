using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using static Apperture.IntegrationTests.Audit.AdministratorGateTestSupport;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP4B-C against real SQL Server, on locking READ COMMITTED and on READ_COMMITTED_SNAPSHOT: one protected attempt reads the
/// attestation once, after the gate is held and inside its transaction; a new attempt reads it again; the decisions of a designation, a
/// strict revocation, a bare reset and an authenticator replacement are made on what the database holds. The reducers themselves are a
/// later package, so each case applies the same statements a reducer will, in the transaction of a real coordinator scope, over the real
/// gate, the real facts reader, the real provider, the real attested reader and the real attestation store. The store is only counted,
/// never simulated, and what a case concludes is read back with SQL it sends itself. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AdministratorViabilityAttemptSqlServerTests : IClassFixture<AuditIsolationCatalogs>
{
    private const string Admin = RoleAuthorityWorld.AdminRoleId;
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public AdministratorViabilityAttemptSqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ------------------------------------------------------------------------------------------------ arrangement

    private async Task<RoleAuthorityWorld> FreshAsync(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await scratch.ExecuteAsync($"""
            DELETE FROM HostConfigurationAttestations;
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            DELETE FROM EntraRegistrationDiagnostics;
            DELETE FROM EntraInstanceRegistrations;
            """);
        return new RoleAuthorityWorld(scratch);
    }

    private static async Task<string> HealthyRecoveryAsync(RoleAuthorityWorld world)
    {
        var id = await world.NewUserAsync("recovery", Admin);
        await world.MakeRecoveryAccessHealthyAsync(id);
        return id;
    }

    /// <summary>An administrator who can sign in only through a proven Entra link.</summary>
    private static async Task<string> EntraOnlyAdminAsync(RoleAuthorityWorld world, string stem)
    {
        var id = await world.NewUserAsync(stem, Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{id}';");
        await world.AddEntraLinkAsync(id, Tenant);
        return id;
    }

    private static Task GiveAuthenticatorAsync(RoleAuthorityWorld world, string userId) =>
        world.Scratch.ExecuteAsync($"""
            INSERT INTO UserTotpCredentials (UserId, ProtectedSecret, ProtectorKeyId, IsEnrolled, CreatedAtUtc)
            VALUES (N'{userId}', 0x01, N'k', 1, SYSDATETIMEOFFSET());
            INSERT INTO UserMfaRecoveryCodes (Id, UserId, CodeHash, Salt, Algorithm, CreatedAtUtc, ConsumedAtUtc)
            VALUES (N'c-{userId}-0', N'{userId}', N'h', N's', N'test', SYSDATETIMEOFFSET(), NULL);
            """);

    private static AttestedEntraOptions UsableEntra() =>
        new(true, "https://login.microsoftonline.com/" + Tenant, "client", true, Tenant, "/signin-oidc", ["openid", "profile"], null, false);

    /// <summary>What a host attests at start: Local established, its Microsoft sign-in options not built yet.</summary>
    private static AttestedAuthenticationPayload AtStart(bool local = true) =>
        new(local, local, UsableEntra(), EntraNow: null);

    /// <summary>What a host attests once real traffic has built its options.</summary>
    private static AttestedAuthenticationPayload Established() =>
        new(true, true, UsableEntra(), new AttestedEntraNow(
            UsableEntra(),
            EntraOptionsBuildQuality.Complete,
            new AttestedHandler(RunningHandlerState.Bound, "https://login.microsoftonline.com/" + Tenant + "/v2.0", "client", true, EntraOptionsBuildQuality.Complete)));

    private static HostConfigurationAttestationWrite Write(AttestedAuthenticationPayload payload, string process = "p1") =>
        new("host-a", process, DateTimeOffset.UtcNow, AttestedAuthenticationPayload.SchemaVersion, payload.Serialize());

    /// <summary>The host writes its attestation, in a session of its own.</summary>
    private static async Task AttestAsync(AuditSqlScratch scratch, AttestedAuthenticationPayload payload)
    {
        await using var db = scratch.NewDb();
        await new SqlHostConfigurationAttestationStore(db).ClaimAsync(Write(payload));
    }

    private static Task AgeAttestationAsync(AuditSqlScratch scratch, int seconds) =>
        scratch.ExecuteAsync($"UPDATE HostConfigurationAttestations SET RefreshedUtc = DATEADD(SECOND, -{seconds}, SYSUTCDATETIME());");

    private static async Task StopHostAsync(AuditSqlScratch scratch)
    {
        await using var db = scratch.NewDb();
        (await new SqlHostConfigurationAttestationStore(db).MarkStoppedAsync("host-a", "p1")).Should().BeTrue();
    }

    /// <summary>The real store, counted, and noting in the log whether it was asked inside a transaction. It changes nothing it returns.</summary>
    private sealed class CountingStore(AppertureDbContext db, AdministratorViabilityWp2SqlServerTests.OrderedLog? log = null) : IHostConfigurationAttestationStore
    {
        public const string Entry = "<<attestation read>>";

        private readonly SqlHostConfigurationAttestationStore _inner = new(db);
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public ValueTask<HostConfigurationAttestationSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            log?.Add(Entry, db.Database.CurrentTransaction is not null);
            return _inner.ReadAsync(cancellationToken);
        }

        public ValueTask ClaimAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => _inner.ClaimAsync(write, cancellationToken);

        public ValueTask<HostAttestationRefreshOutcome> RefreshAsync(HostConfigurationAttestationWrite write, CancellationToken cancellationToken = default) => _inner.RefreshAsync(write, cancellationToken);

        public ValueTask<bool> MarkStoppedAsync(string hostId, string processInstanceId, CancellationToken cancellationToken = default) => _inner.MarkStoppedAsync(hostId, processInstanceId, cancellationToken);
    }

    /// <summary>The emergency command line's composition of the coordinator, over one context.</summary>
    private static AdministratorViabilityCoordinator Coordinator(AppertureDbContext db, IHostConfigurationAttestationStore store, IPlatformAdministratorSetGate? gate = null) =>
        new(
            gate ?? new SqlPlatformAdministratorSetGate(db),
            new AdministratorViabilityService(
                new SqlAdministratorFactsReader(db),
                new EffectiveSignInConfigurationProvider(
                    new AttestedDeploymentAuthenticationConfiguration(store),
                    new SqlEntraInstanceRegistrationRepository(db))));

    /// <summary>A context with no retrying execution strategy, so a case can hold a transaction open across its own steps.</summary>
    private static AppertureDbContext PlainDb(AuditSqlScratch scratch) =>
        new(new DbContextOptionsBuilder<AppertureDbContext>()
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .UseSqlServer(scratch.ConnectionString)
            .Options);

    /// <summary>One attempt of a writer, driven a step at a time: the transaction is open and nothing has been taken yet.</summary>
    private sealed class Attempt : IAsyncDisposable
    {
        private Attempt(AppertureDbContext db, IDbContextTransaction transaction, CountingStore store, AdministratorViabilityCoordinator coordinator)
        {
            Db = db;
            Transaction = transaction;
            Store = store;
            Coordinator = coordinator;
        }

        public AppertureDbContext Db { get; }

        public IDbContextTransaction Transaction { get; }

        public CountingStore Store { get; }

        public AdministratorViabilityCoordinator Coordinator { get; }

        public static async Task<Attempt> BeginAsync(AuditSqlScratch scratch)
        {
            var db = PlainDb(scratch);
            var transaction = await db.Database.BeginTransactionAsync();
            var store = new CountingStore(db);
            return new Attempt(db, transaction, store, AdministratorViabilityAttemptSqlServerTests.Coordinator(db, store));
        }

        /// <summary>Takes the gate and reads the state before the change.</summary>
        public async Task<AdministratorViabilityScope> CaptureAsync()
        {
            var scope = await Coordinator.EnterAsync();
            await scope.CaptureBeforeAsync();
            return scope;
        }

        public Task ApplyAsync(string sql) => Db.Database.ExecuteSqlRawAsync(sql);

        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    private static string ResetSql(string userId) =>
        $"DELETE FROM UserTotpCredentials WHERE UserId = N'{userId}'; DELETE FROM UserMfaRecoveryCodes WHERE UserId = N'{userId}';";

    private static string ReplaceSql(string userId) => $"""
        UPDATE UserTotpCredentials SET ProtectedSecret = 0x02 WHERE UserId = N'{userId}';
        DELETE FROM UserMfaRecoveryCodes WHERE UserId = N'{userId}';
        INSERT INTO UserMfaRecoveryCodes (Id, UserId, CodeHash, Salt, Algorithm, CreatedAtUtc, ConsumedAtUtc)
        VALUES (N'new-{userId}', N'{userId}', N'h', N's', N'test', SYSDATETIMEOFFSET(), NULL);
        """;

    private static string DesignateSql(string userId) => $"UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'{userId}';";

    private static string RevokeSql(string userId) => $"UPDATE Users SET IsBreakGlassEligible = 0 WHERE Id = N'{userId}';";

    private static string DisableSql(string userId) => $"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{userId}';";

    private static Task<int> AuthenticatorRowsAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<int>($"SELECT (SELECT COUNT(*) FROM UserTotpCredentials WHERE UserId = N'{userId}') + (SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE UserId = N'{userId}');");

    private static Task<bool> IsDesignatedAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<bool>($"SELECT IsBreakGlassEligible FROM Users WHERE Id = N'{userId}';");

    // ------------------------------------------------------------------------------------- one reading, after the gate, in the transaction

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task One_attempt_reads_the_attestation_once_after_the_gate_and_inside_its_transaction(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());

        var log = new AdministratorViabilityWp2SqlServerTests.OrderedLog();
        await using var db = PlainDb(world.Scratch);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var store = new CountingStore(db, log);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), log);

        var scope = await Coordinator(db, store, gate).EnterAsync();
        log.Entries.Should().Equal(AdministratorViabilityWp2SqlServerTests.GateEntry);

        await scope.CaptureBeforeAsync();
        await db.Database.ExecuteSqlRawAsync(ResetSql(recovery));
        var decision = await scope.VerifyBareResetAsync(recovery);
        await transaction.CommitAsync();

        store.Reads.Should().Be(1, "the state after the change is judged under the reading the state before was");
        log.Entries.Should().Equal(AdministratorViabilityWp2SqlServerTests.GateEntry, CountingStore.Entry);
        log.OutsideTransaction.Should().BeEmpty("the read joins the unit's transaction");
        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        scope.Captured!.RecoveryHealthy.Should().BeTrue("the stored facts were read before the change");
        scope.After!.RecoveryHealthy.Should().BeFalse("and again after it: only the configuration reading is pinned");
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(0, "the change committed on the server");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_new_attempt_reads_the_evidence_again_after_a_rollback_in_either_direction(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());

        await using var db = PlainDb(world.Scratch);
        var store = new CountingStore(db);
        var coordinator = Coordinator(db, store);

        async Task<ViabilityDecision> RunAndRollBackAsync()
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var scope = await coordinator.EnterAsync();
            await scope.CaptureBeforeAsync();
            await db.Database.ExecuteSqlRawAsync(ResetSql(recovery));
            var decision = await scope.VerifyBareResetAsync(recovery);
            await transaction.RollbackAsync();
            return decision;
        }

        (await RunAndRollBackAsync()).Evidence.Should().Be(ConfigurationEvidence.Attested);

        await StopHostAsync(world.Scratch);
        var withoutEvidence = await RunAndRollBackAsync();
        withoutEvidence.Evidence.Should().Be(ConfigurationEvidence.Unavailable, "the host stopped between the attempts, and the new attempt must see that");
        withoutEvidence.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);

        await AttestAsync(world.Scratch, AtStart());
        (await RunAndRollBackAsync()).Evidence.Should().Be(ConfigurationEvidence.Attested, "and a later attempt does not remember the refusal");

        store.Reads.Should().Be(3);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(2, "every attempt was rolled back");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_confirmed_rollback_of_the_real_unit_reruns_it_and_the_rerun_reads_the_evidence_again(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());

        var attempts = new AttemptCounter();
        await using var db = world.Scratch.NewDb(new RolledBackCommit(), attempts);
        var store = new CountingStore(db);
        var coordinator = Coordinator(db, store);
        var recorded = new List<ViabilityDecision>();

        await NewUnit(db, new Principal(admin), FastOptions()).ExecuteAsync(
            new AuditedOperation(AuditActionNames.MfaReset, AuditActor.EmergencyCli),
            async (transaction, ct) =>
            {
                var scope = await coordinator.EnterAsync(ct);
                await scope.CaptureBeforeAsync(ct);
                await db.Database.ExecuteSqlRawAsync(ResetSql(recovery), ct);
                var decision = await scope.VerifyBareResetAsync(recovery, ct);
                recorded.Add(decision);
                await transaction.StageAsync(AuditEvents.MfaReset(new UserId(recovery), decision.Evidence), ct);
                return true;
            },
            (_, _) => Task.FromResult(true));

        attempts.Started.Should().Be(2, "the rollback was confirmed, so one rerun was allowed");
        store.Reads.Should().Be(2, "a rerun is a new attempt: the reading of the first went with its transaction");
        recorded.Should().HaveCount(2).And.OnlyContain(d => d.Evidence == ConfigurationEvidence.Attested);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(0);

        var row = (await world.AuditRowDetailsAsync(AuditActionNames.MfaReset, recovery)).Should().ContainSingle("the rolled-back attempt left no row").Subject;
        row["Details"].Should().BeOfType<string>().Which.Should().Contain("\"configurationEvidence\":\"attested\"");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_rerun_that_finds_the_host_stopped_is_refused_even_though_the_first_attempt_was_allowed(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());

        await using var db = world.Scratch.NewDb(new RolledBackCommit());
        var store = new CountingStore(db);
        var coordinator = Coordinator(db, store);
        var recorded = new List<ViabilityDecision>();

        var run = () => NewUnit(db, new Principal(admin), FastOptions()).ExecuteAsync(
            new AuditedOperation(AuditActionNames.MfaReset, AuditActor.EmergencyCli),
            async (transaction, ct) =>
            {
                var scope = await coordinator.EnterAsync(ct);
                await scope.CaptureBeforeAsync(ct);
                await db.Database.ExecuteSqlRawAsync(ResetSql(recovery), ct);
                var decision = await scope.VerifyBareResetAsync(recovery, ct);
                recorded.Add(decision);
                if (!decision.IsAllowed)
                    throw decision.Refusal();

                await transaction.StageAsync(AuditEvents.MfaReset(new UserId(recovery), decision.Evidence), ct);
                if (recorded.Count == 1)
                    await StopHostAsync(world.Scratch);
                return true;
            },
            (_, _) => Task.FromResult(true));

        var refusal = (await run.Should().ThrowAsync<GovernanceConflictException>()).Which;

        refusal.Code.Should().Be("administrator_viability_unavailable", "the evidence of the attempt that was rolled back was not carried into the rerun");
        recorded.Select(d => d.Evidence).Should().Equal(ConfigurationEvidence.Attested, ConfigurationEvidence.Unavailable);
        store.Reads.Should().Be(2);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(2, "nothing was changed");
        (await world.AuditRowsAsync(AuditActionNames.MfaReset, recovery)).Should().Be(0);
    }

    // ------------------------------------------------------------------------------------- what happens to the evidence during an attempt

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_host_that_stops_during_an_attempt_does_not_split_it_and_the_next_attempt_sees_the_stop(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());

        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            await attempt.ApplyAsync(ResetSql(recovery));

            await StopHostAsync(world.Scratch);
            var decision = await scope.VerifyBareResetAsync(recovery);

            decision.IsAllowed.Should().BeTrue("a host can stop after the reading: that is the risk the window bounds, and it is accepted");
            decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
            attempt.Store.Reads.Should().Be(1);
            await attempt.Transaction.RollbackAsync();
        }

        await using var next = await Attempt.BeginAsync(world.Scratch);
        var nextScope = await next.CaptureAsync();
        nextScope.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        await next.Transaction.RollbackAsync();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_refresh_during_an_attempt_that_began_without_evidence_cannot_upgrade_it(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());
        await AgeAttestationAsync(world.Scratch, 200);

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        scope.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        await attempt.ApplyAsync(ResetSql(recovery));

        // The host refreshes while the unit holds the gate. It does not wait for the unit, and the unit does not see it.
        await AttestAsync(world.Scratch, AtStart()).WaitAsync(Patience);
        var decision = await scope.VerifyBareResetAsync(recovery);

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        attempt.Store.Reads.Should().Be(1);
        await attempt.Transaction.RollbackAsync();
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Evidence_that_reaches_the_edge_of_its_window_during_an_attempt_refuses_on_one_reading_rather_than_two(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());
        await AgeAttestationAsync(world.Scratch, 175);

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        scope.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Attested, "the reading was fresh when it was read");
        await attempt.ApplyAsync(ResetSql(recovery));

        // The attempt outlasts what the attestation had left. It reads nothing again and it does not keep relying on what it read.
        await Task.Delay(TimeSpan.FromSeconds(5.5));
        var decision = await scope.VerifyBareResetAsync(recovery);

        decision.Verdict.Kind.Should().Be(ViabilityVerdictKind.ConfigurationUnavailable);
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        decision.Refusal().Code.Should().Be("administrator_viability_unavailable");
        attempt.Store.Reads.Should().Be(1);
        scope.Captured!.Trust.Evidence.Should().Be(scope.After!.Trust.Evidence, "the two states agree");
        await attempt.Transaction.RollbackAsync();
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_wait_for_the_gate_does_not_age_the_evidence_because_it_is_read_only_after_the_gate(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, AtStart());
        var holder = await HoldTheGateAsync(world.Scratch);

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var capture = Task.Run(() => attempt.CaptureAsync());
        (await WaitsOnTheGateAsync(world.Scratch, capture)).Should().BeTrue();
        attempt.Store.Reads.Should().Be(0, "nothing is read while the gate is being waited for");

        // The evidence ages out during the wait: a reading made before the gate would still say fresh.
        await AgeAttestationAsync(world.Scratch, 200);
        await holder.CommitAsync();
        await holder.DisposeAsync();
        var scope = await capture.WaitAsync(Patience);

        scope.Captured!.Trust.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        attempt.Store.Reads.Should().Be(1);
        await attempt.Transaction.RollbackAsync();
    }

    // ------------------------------------------------------------------------------------- the bare reset on what the database holds

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_is_decided_on_the_local_reading_while_the_host_has_not_built_its_microsoft_options(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await world.SetActiveRegistrationAsync(Tenant);
        await AttestAsync(world.Scratch, AtStart());

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        scope.Captured!.Trust.EntraKnown.Should().BeFalse("this is the case: the Microsoft reading is not established");
        await attempt.ApplyAsync(ResetSql(recovery));
        var decision = await scope.VerifyBareResetAsync(recovery);
        await attempt.Transaction.CommitAsync();

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        decision.Basis.Should().Be(ViabilityDecisionBasis.LocalSignIn);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_that_leaves_only_microsoft_administrators_is_refused_with_the_microsoft_reading_fully_established(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var entra = await EntraOnlyAdminAsync(world, "entra");
        var recovery = await HealthyRecoveryAsync(world);
        await world.SetActiveRegistrationAsync(Tenant);
        await AttestAsync(world.Scratch, Established());

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        scope.Captured!.Trust.EntraKnown.Should().BeTrue();
        scope.Captured.Now.ViableOrdinaryAdministrators.Should().Contain(entra, "Microsoft sign-in works and this administrator can use it");
        await attempt.ApplyAsync(ResetSql(recovery));
        var decision = await scope.VerifyBareResetAsync(recovery);
        await attempt.Transaction.RollbackAsync();

        decision.Refusal().Code.Should().Be("entra_independent_fallback_required", "a working Microsoft sign-in is not the fallback the invariant asks for");
        decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_that_takes_a_healthy_posture_away_with_no_evidence_is_refused_and_changes_nothing(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        await attempt.ApplyAsync(ResetSql(recovery));
        var decision = await scope.VerifyBareResetAsync(recovery);
        await attempt.Transaction.RollbackAsync();

        decision.Refusal().Code.Should().Be("administrator_viability_unavailable");
        decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_that_cannot_reduce_needs_no_evidence_with_no_host_at_all(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var ordinary = await world.NewUserAsync("ordinary", Admin);
        await GiveAuthenticatorAsync(world, ordinary);
        var recovery = await HealthyRecoveryAsync(world);

        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            await attempt.ApplyAsync(ResetSql(ordinary));
            var decision = await scope.VerifyBareResetAsync(ordinary);
            await attempt.Transaction.CommitAsync();

            decision.IsAllowed.Should().BeTrue("the authenticator of an account that is not the Recovery Access account is not an input of anything");
            decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        }

        await world.Scratch.ExecuteAsync($"UPDATE UserMfaRecoveryCodes SET ConsumedAtUtc = SYSDATETIMEOFFSET() WHERE UserId = N'{recovery}';");
        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            scope.Captured!.RecoveryHealthy.Should().BeFalse("its codes are exhausted");
            await attempt.ApplyAsync(ResetSql(recovery));
            var decision = await scope.VerifyBareResetAsync(recovery);
            await attempt.Transaction.CommitAsync();

            decision.IsAllowed.Should().BeTrue("the posture was already not healthy, so a reset takes nothing away");
            decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        }

        (await AuthenticatorRowsAsync(world.Scratch, ordinary)).Should().Be(0);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_reset_is_not_blamed_for_the_last_code_consumed_at_a_sign_in_during_it(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var ordinary = await world.NewUserAsync("ordinary", Admin);
        await GiveAuthenticatorAsync(world, ordinary);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{ordinary}';");
        var recovery = await HealthyRecoveryAsync(world);

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        scope.Captured!.RecoveryHealthy.Should().BeTrue();
        await attempt.ApplyAsync(ResetSql(ordinary));

        // A legitimate sign-in consumes the last code: it takes no gate, and it is not blocked by a unit that holds it.
        await world.Scratch.ExecuteAsync($"UPDATE UserMfaRecoveryCodes SET ConsumedAtUtc = SYSDATETIMEOFFSET() WHERE UserId = N'{recovery}';").WaitAsync(Patience);
        var decision = await scope.VerifyBareResetAsync(ordinary);
        await attempt.Transaction.CommitAsync();

        scope.After!.RecoveryHealthy.Should().BeFalse("the consumption did happen during the attempt");
        decision.IsAllowed.Should().BeTrue("this reset is of another account and cannot have caused it");
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        (await AuthenticatorRowsAsync(world.Scratch, ordinary)).Should().Be(0);
    }

    // ------------------------------------------------------------------------------------- the replacement

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_replacement_that_leaves_the_posture_healthy_needs_no_host_and_no_local_administrator(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await EntraOnlyAdminAsync(world, "entra");
        var recovery = await HealthyRecoveryAsync(world);

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        await attempt.ApplyAsync(ReplaceSql(recovery));
        var decision = await scope.VerifyReplacementAsync(recovery);
        await attempt.Transaction.CommitAsync();

        decision.IsAllowed.Should().BeTrue();
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        decision.Basis.Should().Be(ViabilityDecisionBasis.PostureOnly);
        scope.After!.RecoveryHealthy.Should().BeTrue();
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserMfaRecoveryCodes WHERE Id = N'new-{recovery}';")).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_replacement_stands_when_a_sign_in_consumes_the_last_code_between_its_reads(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var recovery = await HealthyRecoveryAsync(world);

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        await world.Scratch.ExecuteAsync($"UPDATE UserMfaRecoveryCodes SET ConsumedAtUtc = SYSDATETIMEOFFSET() WHERE UserId = N'{recovery}';").WaitAsync(Patience);
        await attempt.ApplyAsync(ReplaceSql(recovery));
        var decision = await scope.VerifyReplacementAsync(recovery);
        await attempt.Transaction.CommitAsync();

        decision.IsAllowed.Should().BeTrue("the new set replaced the consumed one, so the account is healthy after the switch");
        scope.After!.RecoveryHealthy.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_replacement_that_loses_the_posture_is_refused_and_the_active_authenticator_stays(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var recovery = await HealthyRecoveryAsync(world);
        await AttestAsync(world.Scratch, Established());

        await using var attempt = await Attempt.BeginAsync(world.Scratch);
        var scope = await attempt.CaptureAsync();
        await attempt.ApplyAsync(ResetSql(recovery));
        var decision = await scope.VerifyReplacementAsync(recovery);
        await attempt.Transaction.RollbackAsync();

        decision.Refusal().Code.Should().Be("recovery_access_posture_reduced", "a working evidence reading and a Local administrator do not make this a replacement");
        decision.Evidence.Should().Be(ConfigurationEvidence.NotRequired);
        (await AuthenticatorRowsAsync(world.Scratch, recovery)).Should().Be(2);
    }

    // ------------------------------------------------------------------------------------- designation and strict revocation

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_designation_is_decided_by_the_evidence_it_needs(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var candidate = await world.NewUserAsync("candidate", Admin);
        var spare = await world.NewUserAsync("spare", Admin);

        // Another Local administrator remains: proven safe on the Local reading, with the Microsoft one not established.
        await AttestAsync(world.Scratch, AtStart());
        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            await attempt.ApplyAsync(DesignateSql(candidate));
            var decision = await scope.VerifyDesignationAsync();
            await attempt.Transaction.RollbackAsync();

            decision.IsAllowed.Should().BeTrue();
            decision.Basis.Should().Be(ViabilityDecisionBasis.LocalSignIn);
            decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        }

        // Nobody else can sign in locally, and what Microsoft sign-in can do is not established: refused, not guessed.
        await world.Scratch.ExecuteAsync(DisableSql(spare));
        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            await attempt.ApplyAsync(DesignateSql(candidate));
            var decision = await scope.VerifyDesignationAsync();
            await attempt.Transaction.RollbackAsync();

            decision.Refusal().Code.Should().Be("administrator_viability_unavailable");
            decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        }

        // With the Microsoft reading established, the same designation is judged: nobody would be left able to sign in.
        await AttestAsync(world.Scratch, Established());
        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            await attempt.ApplyAsync(DesignateSql(candidate));
            var decision = await scope.VerifyDesignationAsync();
            await attempt.Transaction.RollbackAsync();

            decision.Refusal().Code.Should().Be("last_viable_administrator");
            decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
            decision.Basis.Should().Be(ViabilityDecisionBasis.FullEvaluation);
        }

        (await IsDesignatedAsync(world.Scratch, candidate)).Should().BeFalse("every attempt was rolled back");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_strict_revocation_needs_another_local_administrator_and_the_local_reading_and_nothing_else(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var entra = await EntraOnlyAdminAsync(world, "entra");
        var recovery = await HealthyRecoveryAsync(world);
        await world.SetActiveRegistrationAsync(Tenant);

        // No evidence at all: refused as unavailable.
        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            await attempt.ApplyAsync(RevokeSql(recovery));
            var decision = await scope.VerifyRevocationAsync(recovery);
            await attempt.Transaction.RollbackAsync();

            decision.Refusal().Code.Should().Be("administrator_viability_unavailable");
            decision.Evidence.Should().Be(ConfigurationEvidence.Unavailable);
        }

        // Full evidence, a working Microsoft sign-in and a healthy posture, but no other Local administrator: the strict rule refuses.
        await AttestAsync(world.Scratch, Established());
        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            scope.Captured!.Now.ViableOrdinaryAdministrators.Should().Contain(entra);
            scope.Captured.RecoveryHealthy.Should().BeTrue();
            await attempt.ApplyAsync(RevokeSql(recovery));
            var decision = await scope.VerifyRevocationAsync(recovery);
            await attempt.Transaction.RollbackAsync();

            decision.Refusal().Code.Should().Be("entra_independent_fallback_required");
            decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
        }

        // Another Local administrator, and a host that has not built its Microsoft options: allowed on the Local reading alone.
        await world.NewUserAsync("local", Admin);
        await AttestAsync(world.Scratch, AtStart());
        await using (var attempt = await Attempt.BeginAsync(world.Scratch))
        {
            var scope = await attempt.CaptureAsync();
            scope.Captured!.Trust.EntraKnown.Should().BeFalse();
            await attempt.ApplyAsync(RevokeSql(recovery));
            var decision = await scope.VerifyRevocationAsync(recovery);
            await attempt.Transaction.CommitAsync();

            decision.IsAllowed.Should().BeTrue();
            decision.Evidence.Should().Be(ConfigurationEvidence.Attested);
            decision.Basis.Should().Be(ViabilityDecisionBasis.LocalSignIn);
        }

        (await IsDesignatedAsync(world.Scratch, recovery)).Should().BeFalse();
    }

    // ------------------------------------------------------------------------------------- the gate serialises the two reductions

    public static IEnumerable<object[]> BothOrdersOnBothModes() =>
        from resetFirst in new[] { true, false }
        from rcsi in new[] { false, true }
        select new object[] { resetFirst, rcsi };

    /// <summary>
    /// Recovery Access is healthy and exactly one administrator is Entra-independent. Each change is safe alone and the two together
    /// leave the installation with neither. Whichever holds the gate first commits; the other, waiting for the gate, is judged on what the
    /// first committed. This is the one pair this package needs; the full matrix of pairs belongs to a later package.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothOrdersOnBothModes))]
    public async Task A_reset_and_a_reduction_of_the_last_independent_administrator_cannot_both_commit(bool resetFirst, bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var scratch = world.Scratch;
        var local = await world.NewUserAsync("local", Admin);
        var entra = await EntraOnlyAdminAsync(world, "entra");
        var recovery = await HealthyRecoveryAsync(world);
        await world.SetActiveRegistrationAsync(Tenant);
        await AttestAsync(scratch, Established());

        await using var first = await Attempt.BeginAsync(scratch);
        var firstScope = await first.CaptureAsync();
        firstScope.Captured!.Now.EntraIndependentAdministrators.Should().BeEquivalentTo([local]);
        firstScope.Captured.Now.ViableOrdinaryAdministrators.Should().Contain(entra, "so the refusal below is the fallback's and not the last administrator's");
        await first.ApplyAsync(resetFirst ? ResetSql(recovery) : DisableSql(local));

        await using var second = await Attempt.BeginAsync(scratch);
        var secondRun = Task.Run(async () =>
        {
            var scope = await second.CaptureAsync();
            await second.ApplyAsync(resetFirst ? DisableSql(local) : ResetSql(recovery));
            return resetFirst
                ? (await scope.VerifyAfterAsync(), scope)
                : ((await scope.VerifyBareResetAsync(recovery)).Verdict, scope);
        });
        (await WaitsOnTheGateAsync(scratch, secondRun)).Should().BeTrue("the second writer waits for the first");
        second.Store.Reads.Should().Be(0);

        var firstVerdict = resetFirst
            ? (await firstScope.VerifyBareResetAsync(recovery)).Verdict
            : await firstScope.VerifyAfterAsync();
        firstVerdict.IsAllowed.Should().BeTrue("the first change is safe by itself");
        await first.Transaction.CommitAsync();

        var (secondVerdict, secondScope) = await secondRun.WaitAsync(Patience);
        secondVerdict.Kind.Should().Be(ViabilityVerdictKind.EntraIndependentFallbackRequired, "it is judged on what the first committed");
        secondScope.Captured!.RecoveryHealthy.Should().Be(!resetFirst, "the state before was read after the gate");
        await second.Transaction.RollbackAsync();

        await using var db = scratch.NewDb();
        var after = await new AdministratorViabilityService(
            new SqlAdministratorFactsReader(db),
            new EffectiveSignInConfigurationProvider(
                new AttestedDeploymentAuthenticationConfiguration(new SqlHostConfigurationAttestationStore(db)),
                new SqlEntraInstanceRegistrationRepository(db))).ReadAsync();
        (after.RecoveryHealthy || after.Now.EntraIndependentAdministrators.Count >= 1).Should().BeTrue("I-FALLBACK holds at the end, whichever came first");
        (await AuthenticatorRowsAsync(scratch, recovery)).Should().Be(resetFirst ? 0 : 2);
        (await world.CredentialDisabledAsync(local)).Should().Be(!resetFirst);
    }
}
