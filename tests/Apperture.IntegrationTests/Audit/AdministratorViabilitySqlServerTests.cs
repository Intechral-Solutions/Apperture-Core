using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// Administrator viability against real SQL Server (IAM-10 gate §3.2; A10.2b.3, A10.2b.4, A10.2b.13, A10.2b.16, rulings D-3 and
/// D-4): the stored facts, the two views, Recovery Access exclusion and health, and the gate's serialization of the invariants. Every
/// case runs on a catalog with the default locking READ COMMITTED and on one with READ_COMMITTED_SNAPSHOT, each case on a catalog of
/// its own because the viability sets are global. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AdministratorViabilitySqlServerTests
{
    private const string Admin = BootstrapConstants.PlatformAdminRoleId;
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "22222222-2222-2222-2222-222222222222";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ---------------------------------------------------------------- seeding

    private static async Task SeedAsync(
        AuditSqlScratch s,
        string id,
        bool admin = true,
        bool credential = false,
        bool credentialDisabled = false,
        string? entraTenant = null,
        bool entraProven = true,
        bool recoveryAccess = false)
    {
        await s.AddUserAsync(id, id);
        if (admin)
            await s.ExecuteAsync($"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'{id}', N'{Admin}');");
        if (credential || credentialDisabled)
        {
            await s.ExecuteAsync($"""
                INSERT INTO LocalCredentials (UserId, PasswordHash, Salt, Algorithm, IsDisabled)
                VALUES (N'{id}', N'hash', N'salt', N'test', {(credentialDisabled ? 1 : 0)});
                """);
        }

        if (entraTenant is not null)
        {
            await s.ExecuteAsync($"""
                INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt, LastAuthenticatedAt)
                VALUES (N'E{id}', N'{id}', 0, N'oid-{id}', N'{entraTenant}', SYSDATETIMEOFFSET(), {(entraProven ? "SYSDATETIMEOFFSET()" : "NULL")});
                """);
        }

        if (recoveryAccess)
            await s.ExecuteAsync($"UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'{id}';");
    }

    private static Task SeedMfaAsync(AuditSqlScratch s, string userId, bool enrolled, int unusedCodes, int consumedCodes = 0) =>
        s.ExecuteAsync($"""
            INSERT INTO UserTotpCredentials (UserId, ProtectedSecret, ProtectorKeyId, IsEnrolled, CreatedAtUtc)
            VALUES (N'{userId}', 0x01, N'k', {(enrolled ? 1 : 0)}, SYSDATETIMEOFFSET());
            {string.Join("\n", Enumerable.Range(0, unusedCodes + consumedCodes).Select(i => $"""
                INSERT INTO UserMfaRecoveryCodes (Id, UserId, CodeHash, Salt, Algorithm, CreatedAtUtc, ConsumedAtUtc)
                VALUES (N'c-{userId}-{i}', N'{userId}', N'h', N's', N'test', SYSDATETIMEOFFSET(), {(i < unusedCodes ? "NULL" : "SYSDATETIMEOFFSET()")});
                """))}
            """);

    private static Task SeedRegistrationAsync(AuditSqlScratch s, string tenant, bool active = true, bool enabled = true) =>
        s.ExecuteAsync($"""
            DELETE FROM EntraRegistrationDiagnostics;
            DELETE FROM EntraInstanceRegistrations;
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, Enabled, IsActive, CreatedUtc, UpdatedUtc, AzureTenantId,
                 AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (N'reg-{tenant[..4]}', N'Registration', N'https://login.microsoftonline.com/{tenant}', N'client', 1,
                    {(enabled ? 1 : 0)}, {(active ? 1 : 0)}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{tenant}', 0, 0);
            """);

    private sealed class StubDeployment(DeploymentAuthenticationReading reading) : IDeploymentAuthenticationConfiguration
    {
        public DeploymentAuthenticationReading Read() => reading;
    }

    /// <summary>A host that started with a usable Entra registration for <paramref name="runningTenant"/>, or a reader that cannot see one.</summary>
    private static DeploymentAuthenticationReading Reading(string? runningTenant, bool local = true)
    {
        var running = runningTenant is null
            ? null
            : new RunningAuthenticationReading(
                new LocalAuthOptions { Enabled = local },
                new EntraAuthOptions
                {
                    Enabled = true,
                    Authority = "https://login.microsoftonline.com/" + runningTenant,
                    ClientId = "client",
                    ClientSecret = "secret",
                    AzureTenantId = runningTenant,
                },
                new EntraAuthOptions
                {
                    Enabled = true,
                    Authority = "https://login.microsoftonline.com/" + runningTenant,
                    ClientId = "client",
                    ClientSecret = "secret",
                    AzureTenantId = runningTenant,
                });

        return new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Trusted,
            [],
            local,
            new EntraAuthOptions { ClientSecret = "deployment-secret" },
            AuthoritativeAboutSecrets: true,
            running);
    }

    private static AdministratorViabilityService Service(AppertureDbContext db, DeploymentAuthenticationReading reading) =>
        new(new SqlAdministratorFactsReader(db), new EffectiveSignInConfigurationProvider(new StubDeployment(reading), new SqlEntraInstanceRegistrationRepository(db)));

    // ---------------------------------------------------------------- facts

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_facts_report_each_administrators_sign_in_inputs_and_only_administrators(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabFacts", rcsi);
        await SeedAsync(s, "local", credential: true);
        await SeedAsync(s, "disabled", credentialDisabled: true, entraTenant: Tenant);
        await SeedAsync(s, "pending", entraTenant: Tenant, entraProven: false);
        await SeedAsync(s, "recovery", credential: true, recoveryAccess: true);
        await SeedAsync(s, "ordinary-user", admin: false, credential: true);

        await using var db = s.NewDb();
        var facts = (await new SqlAdministratorFactsReader(db).ReadAdministratorsAsync()).ToDictionary(f => f.UserId);

        facts.Keys.Should().BeEquivalentTo(["local", "disabled", "pending", "recovery"]);
        facts["local"].Should().BeEquivalentTo(new AdministratorFacts("local", false, true, true, []));
        facts["disabled"].LocalCredentialEnabled.Should().BeFalse();
        facts["disabled"].ProvenEntraLinkTenantIds.Should().Equal(Tenant);
        facts["pending"].ProvenEntraLinkTenantIds.Should().BeEmpty("a link that has never been used to sign in is not proven");
        facts["recovery"].IsRecoveryAccess.Should().BeTrue();
    }

    // ---------------------------------------------------------------- viability

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Viability_counts_who_can_sign_in_not_who_holds_the_role(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabCount", rcsi);
        await SeedRegistrationAsync(s, Tenant);
        await SeedAsync(s, "local", credential: true);
        await SeedAsync(s, "entra", entraTenant: Tenant);
        await SeedAsync(s, "stranded", credentialDisabled: true);
        await SeedAsync(s, "recovery", credential: true, entraTenant: Tenant, recoveryAccess: true);

        await using var db = s.NewDb();
        var state = await Service(db, Reading(Tenant)).ReadAsync();

        foreach (var view in new[] { ViabilityView.Now, ViabilityView.Restart })
        {
            state.For(view).ViableOrdinaryAdministrators.Should().BeEquivalentTo(["local", "entra"], $"{view}: 'stranded' is assigned but cannot sign in, and Recovery Access is never counted");
            state.For(view).EntraIndependentAdministrators.Should().BeEquivalentTo(["local"], view.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_entra_link_counts_only_for_the_tenant_the_sign_in_accepts(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabTenant", rcsi);
        await SeedRegistrationAsync(s, Tenant);
        await SeedAsync(s, "same", entraTenant: Tenant);
        await SeedAsync(s, "padded", entraTenant: "  " + Tenant.ToUpperInvariant() + " ");
        await SeedAsync(s, "other", entraTenant: OtherTenant);

        await using var db = s.NewDb();
        var state = await Service(db, Reading(Tenant)).ReadAsync();

        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["same", "padded"]);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["same", "padded"]);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_tenant_switch_the_host_has_not_restarted_for_is_judged_differently_in_the_two_views(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabSwitch", rcsi);
        // The host started with tenant A; the persisted registration has since been switched to tenant B.
        await SeedRegistrationAsync(s, OtherTenant);
        await SeedAsync(s, "on-a", entraTenant: Tenant);
        await SeedAsync(s, "on-b", entraTenant: OtherTenant);

        await using var db = s.NewDb();
        var state = await Service(db, Reading(Tenant)).ReadAsync();

        state.Now.ViableOrdinaryAdministrators.Should().BeEmpty(
            "the running handler is bound to A while the sign-in checks tokens against B: nobody can sign in with Entra until the restart");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["on-b"], "after the restart tenant B is accepted");
        state.Configuration.Now.Notes.Should().Contain(n => n.Contains("different directory"));
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task With_no_active_registration_no_entra_link_counts_in_either_view(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabNoReg", rcsi);
        await SeedAsync(s, "entra", entraTenant: Tenant);
        await SeedAsync(s, "local", credential: true);

        await using var db = s.NewDb();
        var state = await Service(db, Reading(Tenant)).ReadAsync();

        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["local"]);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo(["local"]);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_command_line_reading_estimates_the_now_view_from_the_persisted_configuration(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabCli", rcsi);
        await SeedRegistrationAsync(s, Tenant);
        await SeedAsync(s, "entra", entraTenant: Tenant);

        await using var db = s.NewDb();
        var state = await Service(db, Reading(runningTenant: null)).ReadAsync();

        state.Configuration.Now.IsEstimate.Should().BeTrue();
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo(state.Restart.ViableOrdinaryAdministrators);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Local_sign_in_disabled_by_the_deployment_leaves_no_entra_independent_administrator(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabNoLocal", rcsi);
        await SeedAsync(s, "local", credential: true);

        await using var db = s.NewDb();
        var state = await Service(db, Reading(runningTenant: null, local: false)).ReadAsync();

        state.Restart.EntraIndependentAdministrators.Should().BeEmpty();
        state.Restart.ViableOrdinaryAdministrators.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- recovery posture

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Recovery_access_health_agrees_with_the_existing_posture_over_every_combination_of_its_inputs(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabPosture", rcsi);
        await s.AddUserAsync("ra", "ra");

        var checkedStates = 0;
        foreach (var designated in new[] { false, true })
        foreach (var role in new[] { false, true })
        foreach (var credential in new[] { "none", "enabled", "disabled" })
        foreach (var mfa in new[] { "none", "pending", "enrolled" })
        foreach (var codes in new[] { 0, 1 })
        {
            await s.ExecuteAsync("""
                DELETE FROM UserMfaRecoveryCodes WHERE UserId = N'ra';
                DELETE FROM UserTotpCredentials WHERE UserId = N'ra';
                DELETE FROM LocalCredentials WHERE UserId = N'ra';
                DELETE FROM UserRoles WHERE UserId = N'ra';
                UPDATE Users SET IsBreakGlassEligible = 0 WHERE Id = N'ra';
                """);
            if (designated)
                await s.ExecuteAsync("UPDATE Users SET IsBreakGlassEligible = 1 WHERE Id = N'ra';");
            if (role)
                await s.ExecuteAsync($"INSERT INTO UserRoles (UserId, RoleId) VALUES (N'ra', N'{Admin}');");
            if (credential != "none")
            {
                await s.ExecuteAsync($"""
                    INSERT INTO LocalCredentials (UserId, PasswordHash, Salt, Algorithm, IsDisabled)
                    VALUES (N'ra', N'h', N's', N'test', {(credential == "disabled" ? 1 : 0)});
                    """);
            }

            if (mfa != "none")
                await SeedMfaAsync(s, "ra", enrolled: mfa == "enrolled", unusedCodes: codes);

            await using var db = s.NewDb();
            var posture = await Emergency(db).GetPostureAsync();
            var facts = await new SqlAdministratorFactsReader(db).ReadRecoveryAccessAsync();

            facts.IsHealthy.Should().Be(
                posture.IsHealthy,
                $"designated {designated}, role {role}, credential {credential}, mfa {mfa}, unused codes {codes}");
            checkedStates++;
        }

        checkedStates.Should().Be(72);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Recovery_access_with_everything_in_place_is_healthy_and_a_consumed_last_code_makes_it_unhealthy(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabCodes", rcsi);
        await SeedAsync(s, "ra", credential: true, recoveryAccess: true);
        await SeedMfaAsync(s, "ra", enrolled: true, unusedCodes: 1);
        await SeedAsync(s, "other", credential: true);

        await using (var db = s.NewDb())
        {
            (await Service(db, Reading(null)).ReadAsync()).RecoveryHealthy.Should().BeTrue();
        }

        // C-14: the consumption of the last code is not refused; the posture is simply unhealthy afterwards.
        await s.ExecuteAsync("UPDATE UserMfaRecoveryCodes SET ConsumedAtUtc = SYSDATETIMEOFFSET() WHERE UserId = N'ra';");

        await using (var db = s.NewDb())
        {
            var state = await Service(db, Reading(null)).ReadAsync();
            state.RecoveryHealthy.Should().BeFalse();
            state.Restart.EntraIndependentAdministrators.Should().BeEquivalentTo(["other"], "the ordinary administrator is the fallback now");
        }
    }

    private static EmergencyAccessService Emergency(AppertureDbContext db) => new(
        new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
        new SqlRoleRepository(db),
        new SqlLocalCredentialRepository(db),
        new LocalPasswordHasher(),
        new NativeMfaService(
            new SqlNativeTotpCredentialRepository(db),
            new SqlNativeMfaRecoveryCodeRepository(db),
            new LocalPasswordHasher(),
            new NoSecrets(),
            TimeProvider.System,
            NullLogger<NativeMfaService>.Instance),
        TimeProvider.System,
        NullLogger<EmergencyAccessService>.Instance);

    /// <summary>The posture reads enrolment and codes only; it never protects or unprotects a secret.</summary>
    private sealed class NoSecrets : ISecretProtector
    {
        public ValueTask<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedPayload, string keyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- the invariant under concurrency

    /// <summary>Two writers that each meet a barrier between reading the state before and applying, to make the overlap certain.</summary>
    private sealed class AsyncBarrier(int participants)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task SignalAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) == participants)
                _all.SetResult();
            return _all.Task.WaitAsync(Patience);
        }
    }

    /// <summary>
    /// A stand-in for a covered writer: optionally take the gate, read the state, disable one Local credential, read the state
    /// again, and commit unless the guard refuses. It exercises exactly the procedure the covered writers will follow.
    /// </summary>
    private static async Task<bool> TryDisableAsync(AuditSqlScratch s, string userId, bool useGate, AsyncBarrier? overlap)
    {
        await using var db = s.NewDb();
        // As the audited unit runs a writer: the retrying strategy wraps the transaction, so a deadlock victim reruns the delegate.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            if (useGate)
                await new SqlPlatformAdministratorSetGate(db).AcquireAsync();

            var service = Service(db, Reading(runningTenant: null));
            var pre = await service.ReadAsync();
            if (overlap is not null)
                await overlap.SignalAndWaitAsync();

            await PlatformAdministratorSetGateSqlServerTests.ExecuteAsync(db, $"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{userId}';");
            var post = await service.ReadAsync();

            if (!AdministratorViabilityGuard.Evaluate(pre, post).IsAllowed)
            {
                await tx.RollbackAsync();
                return false;
            }

            await tx.CommitAsync();
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task With_the_gate_two_concurrent_reductions_of_the_last_two_administrators_commit_exactly_once(bool rcsi)
    {
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabGate", rcsi);
        await SeedAsync(s, "a", credential: true);
        await SeedAsync(s, "b", credential: true);

        var results = await Task.WhenAll(
            Task.Run(() => TryDisableAsync(s, "a", useGate: true, overlap: null)),
            Task.Run(() => TryDisableAsync(s, "b", useGate: true, overlap: null))).WaitAsync(Patience);

        results.Count(committed => committed).Should().Be(1, "the second reduction reads the state the first committed and is refused");
        await using var db = s.NewDb();
        var state = await Service(db, Reading(null)).ReadAsync();
        state.Restart.ViableOrdinaryAdministrators.Should().HaveCount(1);
    }

    [Fact]
    public async Task Without_the_gate_the_same_race_eliminates_the_last_administrator_under_row_versioning()
    {
        // The mutation check: the invariant is only as good as the gate. Both writers read two viable administrators, both
        // disable a different credential, and each then reads a state in which the other's uncommitted change is invisible.
        await using var s = await AuditIsolation.CreateAsync("Apperture_ViabNoGate", rcsi: true);
        await SeedAsync(s, "a", credential: true);
        await SeedAsync(s, "b", credential: true);
        var overlap = new AsyncBarrier(2);

        var results = await Task.WhenAll(
            Task.Run(() => TryDisableAsync(s, "a", useGate: false, overlap)),
            Task.Run(() => TryDisableAsync(s, "b", useGate: false, overlap))).WaitAsync(Patience);

        results.Should().OnlyContain(committed => committed, "without the gate neither writer can see the other");
        await using var db = s.NewDb();
        var state = await Service(db, Reading(null)).ReadAsync();
        state.Restart.ViableOrdinaryAdministrators.Should().BeEmpty("no ordinary administrator can sign in: the invariant the gate protects");
    }
}
