using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP4A against real SQL Server: the Entra-primary policy disables a Local credential only when the shared administrator
/// viability guard allows it, under the global administrator-set gate (IAM-10 gate §3.2; A10.2b.9, A10.2b.10 and the parts of
/// A10.2b.14, A10.2b.15 and A10.2b.17 that belong to this writer). Every case runs on a catalog with the default locking READ
/// COMMITTED and on one with READ_COMMITTED_SNAPSHOT. The viability sets are global, so each case starts by clearing the
/// administrator state of the shared catalog; the audit table is append-only, so audit rows are read by the ids of the case. Fails,
/// never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class EntraPrimaryPolicyViabilitySqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    internal const string Admin = BootstrapConstants.PlatformAdminRoleId;
    internal const string Tenant = "11111111-1111-1111-1111-111111111111";
    internal const string OtherTenant = "22222222-2222-2222-2222-222222222222";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public EntraPrimaryPolicyViabilitySqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    /// <summary>A failing case must not leave an operation parked with its locks: release every pause the case made.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ------------------------------------------------------------------------------------------------ the world

    /// <summary>Clears the administrator state of the shared catalog and seeds the active registration for <see cref="Tenant"/>.</summary>
    private async Task<RoleAuthorityWorld> FreshAsync(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await scratch.ExecuteAsync($"""
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            """);
        var world = new RoleAuthorityWorld(scratch);
        await world.SetActiveRegistrationAsync(Tenant);
        return world;
    }

    /// <summary>An administrator who can sign in with a Local credential and through a proven Entra link of <paramref name="tenant"/>.</summary>
    private static async Task<string> BothWaysAdminAsync(RoleAuthorityWorld world, string stem, string tenant = Tenant, bool proven = true)
    {
        var id = await world.NewUserAsync(stem, Admin);
        await world.AddEntraLinkAsync(id, tenant, proven);
        return id;
    }

    /// <summary>An administrator who can sign in only with a Local credential.</summary>
    private static Task<string> LocalOnlyAdminAsync(RoleAuthorityWorld world, string stem) => world.NewUserAsync(stem, Admin);

    /// <summary>An administrator who can sign in only through a proven Entra link: the Local credential is disabled.</summary>
    private static async Task<string> EntraOnlyAdminAsync(RoleAuthorityWorld world, string stem)
    {
        var id = await world.NewUserAsync(stem, Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{id}';");
        await world.AddEntraLinkAsync(id, Tenant, proven: true);
        return id;
    }

    /// <summary>A person who holds no role, with a Local credential and a proven Entra link.</summary>
    private static async Task<string> OrdinaryUserAsync(RoleAuthorityWorld world, string stem)
    {
        var id = await world.NewUserAsync(stem);
        await world.AddCredentialAsync(id);
        await world.AddEntraLinkAsync(id, Tenant, proven: true);
        return id;
    }

    /// <summary>The designated Recovery Access account: it holds the role and has a credential; healthy when MFA and a recovery code exist.</summary>
    private static async Task<string> RecoveryAccountAsync(RoleAuthorityWorld world, bool healthy)
    {
        var id = await world.NewUserAsync("recovery", Admin);
        if (healthy)
            await world.MakeRecoveryAccessHealthyAsync(id);
        else
            await world.DesignateRecoveryAccessAsync(id);
        return id;
    }

    /// <summary>A host with a usable Entra registration bound to <see cref="Tenant"/>: both views accept that directory.</summary>
    private static DeploymentAuthenticationReading Running() => AdministratorViabilitySqlServerTests.Reading(Tenant);

    private static DeploymentAuthenticationReading LocalSettingUnknown() =>
        new(
            ConfigurationTrustLevel.Unknown,
            ConfigurationTrustLevel.Trusted,
            ["LocalAuth:Enabled is not set in the configuration this reader was started with."],
            true,
            new EntraAuthOptions { ClientSecret = "deployment-secret" },
            AuthoritativeAboutSecrets: true,
            Running: null);

    /// <summary>The active registration names a stored secret the (failing) resolver cannot read: Microsoft sign-in is unknown.</summary>
    private static Task NameAnUnreadableSecretAsync(RoleAuthorityWorld world) =>
        world.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{Guid.NewGuid():D}' WHERE IsActive = 1;");

    // ------------------------------------------------------------------------------------------------ the policy

    private static IAuditEventWriter EventsOf(AuditSqlScratch scratch) =>
        new SqlAuditEventWriter(scratch.Options(), null, HarnessCatalog, null, null, null);

    private static EntraPrimaryAuthenticationPolicy PolicyOver(
        AuditSqlScratch scratch,
        AppertureDbContext db,
        DeploymentAuthenticationReading? reading = null,
        ISecretResolver? resolver = null,
        IPlatformAdministratorSetGate? gate = null,
        IAuditedUnitOfWork? unit = null,
        IAuditEventWriter? events = null)
    {
        reading ??= Running();
        resolver ??= EntraRegistrationSqlHarness.SecretResolver(db);
        return new EntraPrimaryAuthenticationPolicy(
            new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance),
            new SqlLocalCredentialRepository(db),
            NullLogger<EntraPrimaryAuthenticationPolicy>.Instance,
            EntraRegistrationSqlHarness.Viability(db, reading: reading, resolver: resolver, gate: gate),
            AdministratorViabilitySqlServerTests.Service(db, reading, resolver),
            unit ?? NewUnit(db, null),
            events ?? EventsOf(scratch));
    }

    private static async Task<EntraPrimaryLocalDisableOutcome> ApplyAsync(
        AuditSqlScratch scratch,
        string userId,
        DeploymentAuthenticationReading? reading = null,
        ISecretResolver? resolver = null,
        IPlatformAdministratorSetGate? gate = null,
        params IInterceptor[] interceptors)
    {
        await using var db = scratch.NewDb(interceptors);
        return await PolicyOver(scratch, db, reading, resolver, gate).ApplyAfterEntraProvenAsync(new UserId(userId));
    }

    private static Task<int> SuccessRowsAsync(RoleAuthorityWorld world, string userId) =>
        world.AuditRowsAsync(AuditActionNames.LocalDisabledEntraPrimary, userId);

    private static Task<int> DeferralRowsAsync(RoleAuthorityWorld world, string userId) =>
        world.AuditRowsAsync(AuditActionNames.LocalDisableDeferredLastAdmin, userId);

    private static Task<string> HashOfAsync(AuditSqlScratch scratch, string userId) =>
        scratch.ScalarAsync<string>($"SELECT PasswordHash FROM LocalCredentials WHERE UserId = N'{userId}';");

    private static async Task<AdministratorViabilityState> ViabilityAsync(AuditSqlScratch scratch, DeploymentAuthenticationReading? reading = null)
    {
        await using var db = scratch.NewDb();
        return await AdministratorViabilitySqlServerTests.Service(db, reading ?? Running(), EntraRegistrationSqlHarness.SecretResolver(db)).ReadAsync();
    }

    // ================================================================================================ deferral and allowance

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_last_entra_independent_administrator_is_deferred_by_I_FALLBACK_and_a_deferral_is_not_a_change(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "sole");
        var hash = await HashOfAsync(world.Scratch, admin);

        var outcome = await ApplyAsync(world.Scratch, admin);

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse("the disable was rolled back with the unit");
        (await HashOfAsync(world.Scratch, admin)).Should().Be(hash);
        (await SuccessRowsAsync(world, admin)).Should().Be(0, "no successful LocalDisabledEntraPrimary event for a deferral");
        (await DeferralRowsAsync(world, admin)).Should().Be(1, "the W3 attempt row, written after the rollback");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Healthy_recovery_access_satisfies_I_FALLBACK_so_an_administrator_with_a_usable_entra_link_is_disabled(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "sole");
        var recovery = await RecoveryAccountAsync(world, healthy: true);
        var hash = await HashOfAsync(world.Scratch, admin);

        var outcome = await ApplyAsync(world.Scratch, admin);

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await world.CredentialDisabledAsync(admin)).Should().BeTrue();
        (await HashOfAsync(world.Scratch, admin)).Should().Be(hash, "credential material is unchanged");
        (await SuccessRowsAsync(world, admin)).Should().Be(1);
        (await DeferralRowsAsync(world, admin)).Should().Be(0);
        (await world.CredentialDisabledAsync(recovery)).Should().BeFalse("Recovery Access is never disabled");
        var state = await ViabilityAsync(world.Scratch);
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([admin], "the administrator still signs in through Microsoft");
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([admin]);
    }

    public enum UnusableEntra
    {
        /// <summary>The link exists but no sign-in has proven it.</summary>
        PendingLink,

        /// <summary>The link is proven for another directory than the one the sign-in accepts.</summary>
        OtherTenant,

        /// <summary>There is no active registration: no Microsoft token is accepted.</summary>
        NoRegistration,
    }

    public static IEnumerable<object[]> UnusableEntraOnBothModes() =>
        from kind in Enum.GetValues<UnusableEntra>()
        from rcsi in new[] { false, true }
        select new object[] { kind, rcsi };

    [Theory]
    [MemberData(nameof(UnusableEntraOnBothModes))]
    public async Task An_entra_link_that_is_not_a_way_in_makes_the_credential_the_last_viable_one_even_with_healthy_recovery_access(UnusableEntra kind, bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = kind switch
        {
            UnusableEntra.PendingLink => await BothWaysAdminAsync(world, "sole", proven: false),
            UnusableEntra.OtherTenant => await BothWaysAdminAsync(world, "sole", tenant: OtherTenant),
            _ => await BothWaysAdminAsync(world, "sole"),
        };
        if (kind == UnusableEntra.NoRegistration)
            await world.Scratch.ExecuteAsync("DELETE FROM EntraRegistrationDiagnostics; DELETE FROM EntraInstanceRegistrations;");
        await RecoveryAccountAsync(world, healthy: true);

        var outcome = await ApplyAsync(world.Scratch, admin, kind == UnusableEntra.NoRegistration ? AdministratorViabilitySqlServerTests.Reading(null) : null);

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator, "Recovery Access satisfies I-FALLBACK but is not an ordinary administrator, so it cannot satisfy I-VIABLE");
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse();
        (await SuccessRowsAsync(world, admin)).Should().Be(0);
        (await DeferralRowsAsync(world, admin)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Role_holders_who_cannot_sign_in_are_not_the_administrator_who_remains(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "sole");
        var stranded = await LocalOnlyAdminAsync(world, "stranded");
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{stranded}';");
        var noLink = await LocalOnlyAdminAsync(world, "no-local-link");
        await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{noLink}' AND Source = 1;");

        var outcome = await ApplyAsync(world.Scratch, admin);

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator, "three people hold the role, one can sign in without Microsoft");
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_second_administrator_with_an_enabled_local_credential_and_link_lets_the_first_be_disabled_and_a_later_application_proves_it(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");

        var deferred = await ApplyAsync(world.Scratch, admin);
        var second = await LocalOnlyAdminAsync(world, "second");
        var later = await ApplyAsync(world.Scratch, admin);

        deferred.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        later.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "the policy is applied again at every successful Entra sign-in");
        (await world.CredentialDisabledAsync(admin)).Should().BeTrue();
        (await world.CredentialDisabledAsync(second)).Should().BeFalse();
        (await DeferralRowsAsync(world, admin)).Should().Be(1);
        (await SuccessRowsAsync(world, admin)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Exhausting_the_recovery_codes_changes_the_fallback_posture_between_applications(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "sole");
        var recovery = await RecoveryAccountAsync(world, healthy: true);
        await world.Scratch.ExecuteAsync($"UPDATE UserMfaRecoveryCodes SET ConsumedAtUtc = SYSDATETIMEOFFSET() WHERE UserId = N'{recovery}';");

        var exhausted = await ApplyAsync(world.Scratch, admin);
        await world.Scratch.ExecuteAsync($"""
            INSERT INTO UserMfaRecoveryCodes (Id, UserId, CodeHash, Salt, Algorithm, CreatedAtUtc, ConsumedAtUtc)
            VALUES (N'c-{recovery}-new', N'{recovery}', N'h', N's', N'test', SYSDATETIMEOFFSET(), NULL);
            """);
        var restored = await ApplyAsync(world.Scratch, admin);

        exhausted.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        restored.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_recovery_access_account_is_never_disabled_and_the_protection_is_an_attempt_row(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await LocalOnlyAdminAsync(world, "ordinary");
        var recovery = await RecoveryAccountAsync(world, healthy: true);
        await world.AddEntraLinkAsync(recovery, Tenant, proven: true);

        var outcome = await ApplyAsync(world.Scratch, recovery);

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.ProtectedEmergencyAccount);
        (await world.CredentialDisabledAsync(recovery)).Should().BeFalse();
        (await world.AuditRowsAsync(AuditActionNames.RecoveryAccessLocalProtected, recovery)).Should().BeGreaterThanOrEqualTo(1);
        (await SuccessRowsAsync(world, recovery)).Should().Be(0);
    }

    // ================================================================================================ unknown configuration

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_unknown_entra_reading_defers_the_last_local_administrator_as_unavailable_not_as_a_violation(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "sole");
        await NameAnUnreadableSecretAsync(world);
        var hash = await HashOfAsync(world.Scratch, admin);

        var outcome = await ApplyAsync(world.Scratch, admin, resolver: new EntraRegistrationViabilityWp3SqlServerTests.OutageResolver());

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable);
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse();
        (await HashOfAsync(world.Scratch, admin)).Should().Be(hash);
        (await SuccessRowsAsync(world, admin)).Should().Be(0);
        (await DeferralRowsAsync(world, admin)).Should().Be(0, "nothing is proven about the administrators, so no last-administrator row is written");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_unknown_entra_reading_does_not_stop_a_change_that_leaves_a_proven_local_administrator(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");
        await NameAnUnreadableSecretAsync(world);

        var outcome = await ApplyAsync(world.Scratch, admin, resolver: new EntraRegistrationViabilityWp3SqlServerTests.OutageResolver());

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "the safe post-state proof needs no Microsoft reading");
        (await world.CredentialDisabledAsync(admin)).Should().BeTrue();
        (await SuccessRowsAsync(world, admin)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_unknown_local_setting_defers_even_when_another_local_administrator_exists(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");

        var outcome = await ApplyAsync(world.Scratch, admin, LocalSettingUnknown());

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredAdministratorViabilityUnavailable);
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse();
        (await DeferralRowsAsync(world, admin)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_ordinary_user_is_disabled_whatever_the_configuration_says(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await BothWaysAdminAsync(world, "admin");
        var user = await OrdinaryUserAsync(world, "ordinary");
        await NameAnUnreadableSecretAsync(world);

        var outcome = await ApplyAsync(world.Scratch, user, LocalSettingUnknown(), new EntraRegistrationViabilityWp3SqlServerTests.OutageResolver());

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "no administrator-viability input is touched");
        (await world.CredentialDisabledAsync(user)).Should().BeTrue();
        (await SuccessRowsAsync(world, user)).Should().Be(1);
    }

    // ================================================================================================ the presentation

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_account_security_presentation_agrees_with_what_the_policy_then_does_on_the_same_state(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");

        async Task<LocalAuthenticationPresentation> DescribeAsync()
        {
            await using var db = world.Scratch.NewDb();
            var user = await new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance).GetByIdAsync(new UserId(admin));
            var credential = await new SqlLocalCredentialRepository(db).GetByUserIdAsync(new UserId(admin));
            return await PolicyOver(world.Scratch, db).DescribeAsync(user!, credential);
        }

        var sole = await DescribeAsync();
        var deferred = await ApplyAsync(world.Scratch, admin);
        await LocalOnlyAdminAsync(world, "second");
        var withFallback = await DescribeAsync();
        var applied = await ApplyAsync(world.Scratch, admin);

        sole.RecoverySafeguardActive.Should().BeTrue("disabling it would leave nobody who signs in without Microsoft");
        sole.LocalSignInPolicy.Should().Be(AuthenticationMethodStatuses.LocalRecoverySafeguard);
        deferred.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        withFallback.RecoverySafeguardActive.Should().BeFalse();
        withFallback.LocalSignInPolicy.Should().Be(AuthenticationMethodStatuses.LocalEnabled);
        applied.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_presentation_shows_the_safeguard_when_the_entra_link_is_not_a_way_in_even_with_healthy_recovery_access(bool rcsi)
    {
        // The old count-based presentation said "enabled" here, while the policy would keep the credential.
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first", tenant: OtherTenant);
        await RecoveryAccountAsync(world, healthy: true);
        await using var db = world.Scratch.NewDb();
        var user = await new SqlUserRepository(db, NullLogger<SqlUserRepository>.Instance).GetByIdAsync(new UserId(admin));
        var credential = await new SqlLocalCredentialRepository(db).GetByUserIdAsync(new UserId(admin));

        var presentation = await PolicyOver(world.Scratch, db).DescribeAsync(user!, credential);

        presentation.RecoverySafeguardActive.Should().BeTrue();
        (await ApplyAsync(world.Scratch, admin)).Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastViablePlatformAdministrator);
    }

    // ================================================================================================ steady state

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_already_disabled_or_missing_credential_is_a_no_op_that_takes_neither_the_gate_nor_a_unit(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var disabled = await OrdinaryUserAsync(world, "disabled");
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{disabled}';");
        var noCredential = await world.NewUserAsync("entra-only");
        var attempts = new AttemptCounter();

        await using var db = world.Scratch.NewDb(attempts);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        var policy = PolicyOver(world.Scratch, db, gate: gate);

        (await policy.ApplyAfterEntraProvenAsync(new UserId(disabled))).Should().Be(EntraPrimaryLocalDisableOutcome.AlreadyDisabled);
        (await policy.ApplyAfterEntraProvenAsync(new UserId(noCredential))).Should().Be(EntraPrimaryLocalDisableOutcome.NoLocalCredential);
        (await policy.ApplyAfterEntraProvenAsync(new UserId("no-such-user"))).Should().Be(EntraPrimaryLocalDisableOutcome.NoLocalCredential);

        gate.Acquired.Should().Be(0, "the steady state of every later Entra sign-in does not queue on the global gate");
        attempts.Started.Should().Be(0);
        (await SuccessRowsAsync(world, disabled)).Should().Be(0);
        (await DeferralRowsAsync(world, disabled)).Should().Be(0);
    }

    // ================================================================================================ busy

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_busy_gate_is_not_applied_changes_nothing_records_nothing_and_a_later_application_succeeds(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");
        var attempts = new AttemptCounter();

        await using (var holder = await RawSession.BeginAsync(world.Scratch.ConnectionString))
        {
            await EntraRegistrationViabilityWp3SqlServerTests.HoldGateAsync(holder);
            await using var db = world.Scratch.NewDb(attempts);
            var busy = await PolicyOver(world.Scratch, db, gate: new SqlPlatformAdministratorSetGate(db, TimeSpan.FromMilliseconds(300)))
                .ApplyAfterEntraProvenAsync(new UserId(admin));

            busy.Should().Be(EntraPrimaryLocalDisableOutcome.NotAppliedBusy);
            attempts.Started.Should().Be(1, "the busy gate is not retried by the unit");
            (await world.CredentialDisabledAsync(admin)).Should().BeFalse();
            (await SuccessRowsAsync(world, admin)).Should().Be(0, "contention is not a successful enforcement");
            (await DeferralRowsAsync(world, admin)).Should().Be(0, "contention is not a last-administrator deferral");
            await holder.RollbackAsync();
        }

        (await ApplyAsync(world.Scratch, admin)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await SuccessRowsAsync(world, admin)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Cancelling_the_wait_for_the_gate_is_the_callers_cancellation_not_busy(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");

        await using var holder = await RawSession.BeginAsync(world.Scratch.ConnectionString);
        await EntraRegistrationViabilityWp3SqlServerTests.HoldGateAsync(holder);
        using var cancel = new CancellationTokenSource();
        await using var db = world.Scratch.NewDb();
        var waiting = Task.Run(() => PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(admin), cancel.Token).AsTask());
        await PlatformAdministratorSetGateSqlServerTests.WaitForWaitersAsync(world.Scratch, 1, waiting);

        cancel.Cancel();

        await ((Func<Task>)(() => waiting)).Should().ThrowAsync<OperationCanceledException>();
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse();
        (await SuccessRowsAsync(world, admin)).Should().Be(0);
        (await DeferralRowsAsync(world, admin)).Should().Be(0);
        await holder.RollbackAsync();
    }

    // ================================================================================================ M6: the audited unit

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M6_an_audit_insert_failure_leaves_the_credential_enabled_and_nothing_recorded_then_the_policy_succeeds(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");
        var failAudit = new FailingCommand(sql => sql.Contains("INSERT INTO [AuditLogs]", StringComparison.Ordinal));

        await using (var db = world.Scratch.NewDb(failAudit))
        {
            var act = () => PolicyOver(world.Scratch, db).ApplyAfterEntraProvenAsync(new UserId(admin)).AsTask();
            await act.Should().ThrowAsync<Exception>("an infrastructure failure is never turned into a policy outcome");
        }

        failAudit.Failures.Should().BeGreaterThan(0);
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse("an unaudited disable is never committed");
        (await SuccessRowsAsync(world, admin)).Should().Be(0);
        (await ApplyAsync(world.Scratch, admin)).Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        (await SuccessRowsAsync(world, admin)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M6_a_lost_acknowledgement_is_verified_as_committed_and_not_rerun(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");
        var attempts = new AttemptCounter();

        await using var db = world.Scratch.NewDb(new LostCommitAcknowledgement(), attempts);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        var outcome = await PolicyOver(world.Scratch, db, gate: gate).ApplyAfterEntraProvenAsync(new UserId(admin));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled, "a verified commit is never reported as already disabled");
        attempts.Started.Should().Be(1);
        gate.Acquired.Should().Be(1);
        (await world.CredentialDisabledAsync(admin)).Should().BeTrue();
        (await SuccessRowsAsync(world, admin)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M6_a_confirmed_rollback_reruns_the_unit_which_takes_the_gate_again_and_decides_again(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");
        var attempts = new AttemptCounter();

        await using var db = world.Scratch.NewDb(new RolledBackCommit(), attempts);
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), new AdministratorViabilityWp2SqlServerTests.OrderedLog());
        var outcome = await PolicyOver(world.Scratch, db, gate: gate, unit: NewUnit(db, null, FastOptions())).ApplyAfterEntraProvenAsync(new UserId(admin));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.Disabled);
        attempts.Started.Should().Be(2, "verification confirmed the rollback, so one rerun was allowed");
        gate.Acquired.Should().Be(2, "a rerun is a new transaction: the gate it held was released with the rollback and is taken again");
        (await world.CredentialDisabledAsync(admin)).Should().BeTrue();
        (await SuccessRowsAsync(world, admin)).Should().Be(1, "the rolled-back attempt left no row");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M6_an_unknown_outcome_is_not_retried_and_not_turned_into_busy_or_a_deferral(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "first");
        await LocalOnlyAdminAsync(world, "second");
        var attempts = new AttemptCounter();

        await using var db = world.Scratch.NewDb(new LostCommitAcknowledgement(), attempts);
        var act = () => PolicyOver(world.Scratch, db, unit: NewUnit(db, null, FastOptions(), verification: new UnreachableVerification()))
            .ApplyAfterEntraProvenAsync(new UserId(admin)).AsTask();

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
        attempts.Started.Should().Be(1);
        // The commit really happened here: the credential and its row land together, never one without the other.
        ((await world.CredentialDisabledAsync(admin)), await SuccessRowsAsync(world, admin)).Should().Be((true, 1));
        (await DeferralRowsAsync(world, admin)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task W3_a_deferral_is_one_attempt_row_after_a_rolled_back_unit_with_no_success_row_and_the_policy_actor(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "sole");

        await ApplyAsync(world.Scratch, admin);

        var rows = await world.Scratch.RowsAsync(
            $"SELECT Action, Actor, ActorKind, OutcomeCode FROM AuditLogs WHERE TargetType = N'user' AND TargetId = N'{admin}' ORDER BY Id;");
        var row = rows.Should().ContainSingle().Which;
        row["Action"].Should().Be(AuditActionNames.LocalDisableDeferredLastAdmin);
        row["Actor"].Should().Be("system:entra-primary-policy");
        row["ActorKind"].Should().Be("system");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_failing_deferral_row_does_not_change_what_the_caller_sees_and_the_credential_stays_enabled(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var admin = await BothWaysAdminAsync(world, "sole");

        await using var db = world.Scratch.NewDb();
        var outcome = await PolicyOver(world.Scratch, db, events: new UnwritableEvents()).ApplyAfterEntraProvenAsync(new UserId(admin));

        outcome.Should().Be(EntraPrimaryLocalDisableOutcome.DeferredLastEntraIndependentPlatformAdministrator);
        (await world.CredentialDisabledAsync(admin)).Should().BeFalse();
    }

    private sealed class UnwritableEvents : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected: the audit store is unavailable.");

        public Task<bool> TryWriteAsync(AuditEvent auditEvent, AuditActor actor, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
