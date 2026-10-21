using System.Data.Common;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP2 against real SQL Server: the person-driven writers of administrator viability (removing the Platform Administrator
/// role, disabling a Local credential, removing a Local or an Entra identity link, and the raise-only writers beside them) decide
/// under the global administrator-set gate (IAM-10 gate §3.2; A10.2b.1 to A10.2b.6, A10.2b.8, and the parts of A10.2b.14, A10.2b.15
/// and A10.2b.17 that belong to these writers). Every case runs on a catalog with the default locking READ COMMITTED and on one with
/// READ_COMMITTED_SNAPSHOT. The viability sets are global, so each case starts by clearing the administrator state of the shared
/// catalog; the audit table is append-only, so audit rows are read by the ids of the case. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class AdministratorViabilityWp2SqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    internal const string Admin = BootstrapConstants.PlatformAdminRoleId;
    internal const string Tenant = "11111111-1111-1111-1111-111111111111";
    internal const string OtherTenant = "22222222-2222-2222-2222-222222222222";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public AdministratorViabilityWp2SqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    /// <summary>A failing case must not leave an operation parked with its locks: release every pause the case made.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ------------------------------------------------------------------------------------------------ the writers

    public enum Op
    {
        RemoveRole,
        DisableCredential,
        RemoveLocalLink,
        RemoveEntraLink,
    }

    private static string SuccessAction(Op op) => op switch
    {
        Op.RemoveRole => AuditActionNames.RoleRemoved,
        Op.DisableCredential => AuditActionNames.CredentialDisabled,
        _ => AuditActionNames.IdentityLinkRemoved,
    };

    private static string BlockedAction(Op op) => op switch
    {
        Op.RemoveRole => AuditActionNames.BlockedLastPlatformAdminRemoval,
        Op.DisableCredential => AuditActionNames.BlockedLastAdminCredentialDisabled,
        _ => AuditActionNames.BlockedLastAdminLinkRemoved,
    };

    /// <summary>The stable code of a refusal that leaves nobody able to sign in, for this writer.</summary>
    private static string RefusalCode(Op op) => op switch
    {
        Op.RemoveRole => "last_viable_administrator",
        Op.DisableCredential => "last_local_credential",
        _ => "last_viable_sign_in_method",
    };

    /// <summary>What the audit rows of the writer name as their target: the user, or the link (<c>L</c>/<c>E</c> plus the user id in these catalogs).</summary>
    private static string AuditTarget(Op op, string userId) => op switch
    {
        Op.RemoveLocalLink => "L" + userId,
        Op.RemoveEntraLink => "E" + userId,
        _ => userId,
    };

    private sealed class AllowingStrongAuthentication : IStrongAuthenticationGate
    {
        public ValueTask<AuthenticationAssurance> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuthenticationAssurance("test", true, DateTimeOffset.UtcNow, "c1", true));

        public ValueTask RequireRecentStrongAuthenticationAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private static readonly AllowingStrongAuthentication StrongAuthentication = new();

    private static UserManagementService Service(
        RoleAuthorityWorld world,
        AppertureDbContext db,
        string actor,
        DeploymentAuthenticationReading? reading = null,
        IPlatformAdministratorSetGate? gate = null,
        IAuditedUnitOfWork? unit = null,
        IAuditEventWriter? events = null,
        IAdministratorViabilityCoordinator? coordinator = null) =>
        world.Service(db, actor, advisoryAllowsAll: true, unit: unit, events: events, gate: gate, deployment: reading, strongAuthentication: StrongAuthentication, coordinator: coordinator);

    private static async Task<string> UsernameOfAsync(RoleAuthorityWorld world, string userId) =>
        await world.Scratch.ScalarAsync<string>($"SELECT Username FROM Users WHERE Id = N'{userId}';");

    /// <summary>Runs one writer on the context it is given, as <paramref name="actor"/>, on <paramref name="target"/>.</summary>
    private static async Task RunAsync(
        RoleAuthorityWorld world,
        Op op,
        string actor,
        string target,
        AppertureDbContext db,
        DeploymentAuthenticationReading? reading = null,
        IPlatformAdministratorSetGate? gate = null,
        IAuditedUnitOfWork? unit = null,
        IAuditEventWriter? events = null,
        CancellationToken cancellationToken = default)
    {
        var service = Service(world, db, actor, reading, gate, unit, events);
        switch (op)
        {
            case Op.RemoveRole:
                await service.RemoveRoleAsync(new UserId(target), new RoleId(Admin), cancellationToken);
                break;
            case Op.DisableCredential:
                await service.SetLocalCredentialDisabledAsync(new UserId(target), disabled: true, cancellationToken);
                break;
            case Op.RemoveLocalLink:
                await service.RemoveIdentityLinkAsync(new UserId(target), IdentitySource.Local, await UsernameOfAsync(world, target), cancellationToken);
                break;
            default:
                await service.RemoveIdentityLinkAsync(new UserId(target), IdentitySource.Entra, "oid-" + target, cancellationToken);
                break;
        }
    }

    /// <summary>Whether the effect of the writer is in the catalog for <paramref name="userId"/>.</summary>
    private static async Task<bool> AppliedAsync(RoleAuthorityWorld world, Op op, string userId) => op switch
    {
        Op.RemoveRole => !await world.AssignmentExistsAsync(userId, Admin),
        Op.DisableCredential => await world.CredentialDisabledAsync(userId),
        Op.RemoveLocalLink => await world.LinkCountAsync(userId, 1) == 0,
        _ => await world.LinkCountAsync(userId, 0) == 0,
    };

    // ---------------------------------------------------------------------------------------------- arrangement

    /// <summary>Clears the administrator state of the shared catalog: the role assignments, the posture inputs and the registrations.</summary>
    private async Task<RoleAuthorityWorld> FreshAsync(bool rcsi)
    {
        var scratch = _catalogs.For(rcsi);
        await scratch.ExecuteAsync($"""
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            DELETE FROM EntraRegistrationDiagnostics;
            DELETE FROM EntraInstanceRegistrations;
            """);
        return new RoleAuthorityWorld(scratch);
    }

    private static Task DisableCredentialRawAsync(RoleAuthorityWorld world, string userId) =>
        world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{userId}';");

    /// <summary>An administrator who can sign in with a Local credential (and so is Entra-independent).</summary>
    private static Task<string> LocalAdminAsync(RoleAuthorityWorld world, string stem) => world.NewUserAsync(stem, Admin);

    /// <summary>An administrator who holds the role but cannot sign in: the credential is disabled and there is no Entra link.</summary>
    private static async Task<string> StrandedAdminAsync(RoleAuthorityWorld world, string stem)
    {
        var id = await world.NewUserAsync(stem, Admin);
        await DisableCredentialRawAsync(world, id);
        return id;
    }

    /// <summary>An administrator who can sign in only through a proven Entra link (the Local credential is disabled).</summary>
    private static async Task<string> EntraOnlyAdminAsync(RoleAuthorityWorld world, string stem, string tenant = Tenant)
    {
        var id = await StrandedAdminAsync(world, stem);
        await world.AddEntraLinkAsync(id, tenant, proven: true);
        return id;
    }

    /// <summary>The designated Recovery Access account, with the role, a credential, MFA and a recovery code: demonstrably healthy.</summary>
    private static async Task<string> HealthyRecoveryAsync(RoleAuthorityWorld world)
    {
        var id = await world.NewUserAsync("recovery", Admin);
        await world.MakeRecoveryAccessHealthyAsync(id);
        return id;
    }

    /// <summary>The designated Recovery Access account, which holds the role and has a credential but no MFA: not healthy.</summary>
    private static async Task<string> UnhealthyRecoveryAsync(RoleAuthorityWorld world)
    {
        var id = await world.NewUserAsync("recovery", Admin);
        await world.DesignateRecoveryAccessAsync(id);
        return id;
    }

    /// <summary>A user who needs a writer to be a legitimate actor: an administrator who cannot sign in, so who counts for nothing.</summary>
    private static Task<string> ActorAsync(RoleAuthorityWorld world, string stem) => StrandedAdminAsync(world, stem);

    /// <summary>A target who is viable only by the path <paramref name="op"/> removes.</summary>
    private static async Task<string> TargetOfAsync(RoleAuthorityWorld world, Op op, string stem)
    {
        switch (op)
        {
            case Op.RemoveEntraLink:
                return await EntraOnlyAdminAsync(world, stem);
            case Op.RemoveLocalLink:
            {
                // A second link (pending, so not a sign-in path) lets the Local link go without emptying the user.
                var id = await world.NewUserAsync(stem, Admin);
                await world.AddEntraLinkAsync(id, Tenant, proven: false);
                return id;
            }

            default:
                return await world.NewUserAsync(stem, Admin);
        }
    }

    private static DeploymentAuthenticationReading EntraReading => AdministratorViabilitySqlServerTests.Reading(Tenant);

    /// <summary>The host's start-up reading when the Local setting could not be established.</summary>
    private static DeploymentAuthenticationReading LocalSettingUnknown() =>
        new(
            ConfigurationTrustLevel.Unknown,
            ConfigurationTrustLevel.Trusted,
            ["LocalAuth:Enabled is not set in the configuration this reader was started with."],
            true,
            new EntraAuthOptions(),
            AuthoritativeAboutSecrets: true,
            Running: null);

    /// <summary>A host that started with Entra switched off, while the persisted registration (the Restart view) is usable.</summary>
    private static DeploymentAuthenticationReading EntraOffNowOnAfterRestart() =>
        new(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Trusted,
            [],
            true,
            new EntraAuthOptions { ClientSecret = "deployment-secret" },
            AuthoritativeAboutSecrets: true,
            new RunningAuthenticationReading(new LocalAuthOptions { Enabled = true }, new EntraAuthOptions { Enabled = false }, RunningHandlerBinding.Disabled()));

    // ------------------------------------------------------------------------------------------------ reading state

    /// <summary>A stable text of every row a writer here can change, so that a refusal can be shown to have changed nothing at all.</summary>
    private static Task<string> StateAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<string>("""
            SELECT CONCAT(
                (SELECT STRING_AGG(CAST(CONCAT(UserId, '/', RoleId) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY UserId, RoleId) FROM UserRoles), '|',
                (SELECT STRING_AGG(CAST(CONCAT(UserId, ':', IsDisabled) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY UserId) FROM LocalCredentials), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id, ':', UserId, ':', Source, ':', CASE WHEN LastAuthenticatedAt IS NULL THEN 0 ELSE 1 END) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM IdentityLinks), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id, ':', IsBreakGlassEligible) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM Users));
            """);

    /// <summary>The viability state as the host reads it, for the configuration the case runs under.</summary>
    private static async Task<AdministratorViabilityState> ViabilityAsync(AuditSqlScratch scratch, DeploymentAuthenticationReading? reading = null)
    {
        await using var db = scratch.NewDb();
        return await AdministratorViabilitySqlServerTests.Service(db, reading ?? AdministratorViabilitySqlServerTests.Reading(null)).ReadAsync();
    }

    private static async Task<GovernanceConflictException> RefusedAsync(Func<Task> act)
    {
        var thrown = await act.Should().ThrowAsync<GovernanceConflictException>();
        return thrown.Which;
    }

    // ================================================================================================ sequential

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A3_two_assigned_administrators_when_only_one_can_sign_in_removing_the_viable_ones_role_is_refused(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var stranded = await StrandedAdminAsync(world, "stranded");
        var viable = await LocalAdminAsync(world, "viable");
        (await world.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM UserRoles WHERE RoleId = N'{Admin}';")).Should().Be(2, "two assignments exist");
        var before = await StateAsync(world.Scratch);

        await using var db = world.Scratch.NewDb();
        var refusal = await RefusedAsync(() => RunAsync(world, Op.RemoveRole, stranded, viable, db));

        refusal.Code.Should().Be("last_viable_administrator");
        refusal.Message.Should().NotBeNullOrWhiteSpace().And.Contain("sign in");
        (await StateAsync(world.Scratch)).Should().Be(before, "the refused removal was applied and rolled back, and nothing else moved");
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastPlatformAdminRemoval, viable)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.RoleRemoved, viable)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_the_role_of_an_administrator_who_cannot_sign_in_is_allowed_while_one_who_can_remains(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var stranded = await StrandedAdminAsync(world, "stranded");
        var viable = await LocalAdminAsync(world, "viable");

        await using var db = world.Scratch.NewDb();
        await RunAsync(world, Op.RemoveRole, viable, stranded, db);

        (await world.AssignmentExistsAsync(stranded, Admin)).Should().BeFalse();
        (await world.AuditRowsAsync(AuditActionNames.RoleRemoved, stranded)).Should().Be(1);
        (await ViabilityAsync(world.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([viable]);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A5_when_nobody_can_sign_in_a_removal_is_never_refused_so_the_instance_can_always_be_repaired(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var first = await StrandedAdminAsync(world, "first");
        var second = await StrandedAdminAsync(world, "second");

        await using var db = world.Scratch.NewDb();
        await RunAsync(world, Op.RemoveRole, first, second, db);

        (await world.AssignmentExistsAsync(second, Admin)).Should().BeFalse("the state already violated I-VIABLE, which is no reason to refuse");
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastPlatformAdminRemoval, second)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A5_a_no_op_for_the_last_viable_administrator_is_silent_and_a_real_removal_of_them_is_not(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var sole = await LocalAdminAsync(world, "sole");
        var bystander = await world.NewUserAsync("bystander");
        var before = await StateAsync(world.Scratch);

        await using (var db = world.Scratch.NewDb())
            await RunAsync(world, Op.RemoveRole, actor, bystander, db);

        (await StateAsync(world.Scratch)).Should().Be(before);
        (await world.AuditRowsAsync(AuditActionNames.RoleRemoved, bystander)).Should().Be(0);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastPlatformAdminRemoval, bystander)).Should().Be(0, "a no-op is not a recorded refusal (the AUD-1.2 finding)");

        await using var db2 = world.Scratch.NewDb();
        (await RefusedAsync(() => RunAsync(world, Op.RemoveRole, actor, sole, db2))).Code.Should().Be("last_viable_administrator");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A2_the_sequential_hole_is_closed_two_local_only_administrators_cannot_both_lose_their_credential(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var first = await LocalAdminAsync(world, "first");
        var second = await LocalAdminAsync(world, "second");
        await HealthyRecoveryAsync(world);

        await using (var db = world.Scratch.NewDb())
            await RunAsync(world, Op.DisableCredential, actor, first, db);
        await using var db2 = world.Scratch.NewDb();
        var refusal = await RefusedAsync(() => RunAsync(world, Op.DisableCredential, actor, second, db2));

        refusal.Code.Should().Be("last_local_credential");
        refusal.Message.Should().Contain("sign-in method");
        (await world.CredentialDisabledAsync(first)).Should().BeTrue();
        (await world.CredentialDisabledAsync(second)).Should().BeFalse("the refused disable was rolled back");
        (await world.AuditRowsAsync(AuditActionNames.CredentialDisabled, first)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.CredentialDisabled, second)).Should().Be(0);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminCredentialDisabled, second)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task F9_disabling_an_already_disabled_credential_is_a_silent_no_op_even_for_an_administrator_nobody_can_replace(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var sole = await StrandedAdminAsync(world, "sole");
        var before = await StateAsync(world.Scratch);

        await using var db = world.Scratch.NewDb();
        await RunAsync(world, Op.DisableCredential, sole, sole, db);

        (await StateAsync(world.Scratch)).Should().Be(before);
        (await world.AuditRowsAsync(AuditActionNames.CredentialDisabled, sole)).Should().Be(0);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminCredentialDisabled, sole)).Should().Be(0, "the no-op is decided before the invariants, so it is not recorded as a refusal");
    }

    public static IEnumerable<object[]> CredentialAlternatives()
    {
        foreach (var rcsi in new[] { false, true })
        {
            // name, Recovery Access posture, expected outcome (null = allowed)
            yield return new object[] { "entra_proven_usable_recovery_unhealthy", rcsi, "entra_independent_fallback_required" };
            yield return new object[] { "entra_proven_usable_recovery_healthy", rcsi, null! };
            yield return new object[] { "entra_proven_other_tenant", rcsi, "last_local_credential" };
            yield return new object[] { "entra_pending", rcsi, "last_local_credential" };
            yield return new object[] { "no_registration", rcsi, "last_local_credential" };
            yield return new object[] { "running_host_has_entra_off", rcsi, "last_local_credential" };
        }
    }

    [Theory]
    [MemberData(nameof(CredentialAlternatives))]
    public async Task A8_disabling_a_credential_asks_whether_the_entra_alternative_really_works_and_whether_a_fallback_remains(string scenario, bool rcsi, string? refusalCode)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var target = await world.NewUserAsync("target", Admin);
        var reading = EntraReading;
        switch (scenario)
        {
            case "entra_proven_usable_recovery_unhealthy":
                await world.SetActiveRegistrationAsync(Tenant);
                await world.AddEntraLinkAsync(target, Tenant);
                await UnhealthyRecoveryAsync(world);
                break;
            case "entra_proven_usable_recovery_healthy":
                await world.SetActiveRegistrationAsync(Tenant);
                await world.AddEntraLinkAsync(target, Tenant);
                await HealthyRecoveryAsync(world);
                break;
            case "entra_proven_other_tenant":
                await world.SetActiveRegistrationAsync(Tenant);
                await world.AddEntraLinkAsync(target, OtherTenant);
                await HealthyRecoveryAsync(world);
                break;
            case "entra_pending":
                await world.SetActiveRegistrationAsync(Tenant);
                await world.AddEntraLinkAsync(target, Tenant, proven: false);
                await HealthyRecoveryAsync(world);
                break;
            case "no_registration":
                await world.AddEntraLinkAsync(target, Tenant);
                await HealthyRecoveryAsync(world);
                reading = AdministratorViabilitySqlServerTests.Reading(null);
                break;
            default:
                // The registration is usable after a restart, but the running host has Entra off: nobody signs in through Entra now.
                await world.SetActiveRegistrationAsync(Tenant);
                await world.AddEntraLinkAsync(target, Tenant);
                await HealthyRecoveryAsync(world);
                reading = EntraOffNowOnAfterRestart();
                break;
        }

        var before = await StateAsync(world.Scratch);
        await using var db = world.Scratch.NewDb();
        var act = () => RunAsync(world, Op.DisableCredential, actor, target, db, reading);

        if (refusalCode is null)
        {
            await act();
            (await world.CredentialDisabledAsync(target)).Should().BeTrue();
            (await world.AuditRowsAsync(AuditActionNames.CredentialDisabled, target)).Should().Be(1);
        }
        else
        {
            (await RefusedAsync(act)).Code.Should().Be(refusalCode);
            (await StateAsync(world.Scratch)).Should().Be(before);
            (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminCredentialDisabled, target)).Should().Be(1);
            (await world.AuditRowsAsync(AuditActionNames.CredentialDisabled, target)).Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A8_with_two_entra_independent_administrators_the_first_reduction_is_allowed_and_the_second_refused(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await world.SetActiveRegistrationAsync(Tenant);
        var first = await world.NewUserAsync("first", Admin);
        var second = await world.NewUserAsync("second", Admin);
        await world.AddEntraLinkAsync(first, Tenant);
        await world.AddEntraLinkAsync(second, Tenant);
        await UnhealthyRecoveryAsync(world);

        await using (var db = world.Scratch.NewDb())
            await RunAsync(world, Op.DisableCredential, actor, first, db, EntraReading);
        await using var db2 = world.Scratch.NewDb();
        var refusal = await RefusedAsync(() => RunAsync(world, Op.DisableCredential, actor, second, db2, EntraReading));

        refusal.Code.Should().Be("entra_independent_fallback_required");
        refusal.Message.Should().Contain("local credential").And.Contain("Recovery Access");
        (await world.CredentialDisabledAsync(first)).Should().BeTrue();
        (await world.CredentialDisabledAsync(second)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A8_removing_the_role_of_the_only_entra_independent_administrator_is_refused_unless_recovery_access_is_healthy(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await world.SetActiveRegistrationAsync(Tenant);
        var localAdmin = await world.NewUserAsync("local", Admin);
        var entraAdmin = await EntraOnlyAdminAsync(world, "entra");
        var recovery = await UnhealthyRecoveryAsync(world);

        await using (var db = world.Scratch.NewDb())
        {
            var refusal = await RefusedAsync(() => RunAsync(world, Op.RemoveRole, actor, localAdmin, db, EntraReading));
            refusal.Code.Should().Be("entra_independent_fallback_required");
        }

        await world.MakeRecoveryAccessHealthyAsync(recovery);
        await using var db2 = world.Scratch.NewDb();
        await RunAsync(world, Op.RemoveRole, actor, localAdmin, db2, EntraReading);

        (await world.AssignmentExistsAsync(localAdmin, Admin)).Should().BeFalse("with Recovery Access healthy the IAM-3 transition rule allows it");
        (await ViabilityAsync(world.Scratch, EntraReading)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([entraAdmin]);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A4_recovery_access_is_never_counted_as_an_administrator_or_as_the_fallback(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var ordinary = await LocalAdminAsync(world, "ordinary");
        // Healthy, holding the role and able to sign in: and still not counted.
        await HealthyRecoveryAsync(world);

        await using (var db = world.Scratch.NewDb())
            (await RefusedAsync(() => RunAsync(world, Op.RemoveRole, actor, ordinary, db))).Code.Should().Be("last_viable_administrator");
        await using (var db = world.Scratch.NewDb())
            (await RefusedAsync(() => RunAsync(world, Op.DisableCredential, actor, ordinary, db))).Code.Should().Be("last_local_credential");

        (await world.AssignmentExistsAsync(ordinary, Admin)).Should().BeTrue();
        (await world.CredentialDisabledAsync(ordinary)).Should().BeFalse();
    }

    // ---- identity links ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A6_removing_the_local_link_of_the_last_viable_administrator_is_refused_and_a_pending_entra_link_does_not_save_it(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var target = await TargetOfAsync(world, Op.RemoveLocalLink, "target");
        await world.SetActiveRegistrationAsync(Tenant);
        var before = await StateAsync(world.Scratch);

        await using var db = world.Scratch.NewDb();
        var refusal = await RefusedAsync(() => RunAsync(world, Op.RemoveLocalLink, actor, target, db, EntraReading));

        refusal.Code.Should().Be("last_viable_sign_in_method");
        refusal.Message.Should().Contain("used to sign in", "the message says why a pending identity does not count");
        (await StateAsync(world.Scratch)).Should().Be(before);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminLinkRemoved, "L" + target)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "L" + target)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A6_removing_the_entra_link_of_the_last_viable_administrator_is_refused_it_was_unguarded(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await world.SetActiveRegistrationAsync(Tenant);
        var target = await EntraOnlyAdminAsync(world, "target");
        await HealthyRecoveryAsync(world);
        var before = await StateAsync(world.Scratch);

        await using var db = world.Scratch.NewDb();
        var refusal = await RefusedAsync(() => RunAsync(world, Op.RemoveEntraLink, actor, target, db, EntraReading));

        refusal.Code.Should().Be("last_viable_sign_in_method");
        (await StateAsync(world.Scratch)).Should().Be(before, "the Entra link is still there");
        (await world.LinkCountAsync(target, 0)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminLinkRemoved, "E" + target)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_an_entra_link_is_allowed_when_another_administrator_can_sign_in_or_when_the_link_never_counted(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await world.SetActiveRegistrationAsync(Tenant);
        var counted = await EntraOnlyAdminAsync(world, "counted");
        var spare = await LocalAdminAsync(world, "spare");
        var uncounted = await world.NewUserAsync("uncounted", Admin);
        await world.AddEntraLinkAsync(uncounted, OtherTenant);
        await HealthyRecoveryAsync(world);

        await using (var db = world.Scratch.NewDb())
            await RunAsync(world, Op.RemoveEntraLink, actor, counted, db, EntraReading);
        await using (var db = world.Scratch.NewDb())
            await RunAsync(world, Op.RemoveEntraLink, actor, uncounted, db, EntraReading);

        (await world.LinkCountAsync(counted, 0)).Should().Be(0);
        (await world.LinkCountAsync(uncounted, 0)).Should().Be(0);
        (await world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "E" + counted)).Should().Be(1);
        (await world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "E" + uncounted)).Should().Be(1);
        (await ViabilityAsync(world.Scratch, EntraReading)).Restart.ViableOrdinaryAdministrators.Should().Contain(spare);
    }

    public static IEnumerable<object[]> LocalLinkWithProvenEntra()
    {
        foreach (var rcsi in new[] { false, true })
        {
            yield return new object[] { rcsi, false, "entra_independent_fallback_required" };
            yield return new object[] { rcsi, true, null! };
        }
    }

    [Theory]
    [MemberData(nameof(LocalLinkWithProvenEntra))]
    public async Task A8_removing_the_local_link_that_leaves_only_entra_asks_whether_recovery_access_can_stand_in(bool rcsi, bool recoveryHealthy, string? refusalCode)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await world.SetActiveRegistrationAsync(Tenant);
        var target = await world.NewUserAsync("target", Admin);
        await world.AddEntraLinkAsync(target, Tenant);
        _ = recoveryHealthy ? await HealthyRecoveryAsync(world) : await UnhealthyRecoveryAsync(world);

        await using var db = world.Scratch.NewDb();
        var act = () => RunAsync(world, Op.RemoveLocalLink, actor, target, db, EntraReading);

        if (refusalCode is null)
        {
            await act();
            (await world.LinkCountAsync(target, 1)).Should().Be(0);
            (await world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "L" + target)).Should().Be(1);
        }
        else
        {
            (await RefusedAsync(act)).Code.Should().Be(refusalCode);
            (await world.LinkCountAsync(target, 1)).Should().Be(1);
            (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminLinkRemoved, "L" + target)).Should().Be(1);
        }
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_a_link_that_does_not_exist_is_a_silent_no_op_that_does_not_consult_the_invariants(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var sole = await LocalAdminAsync(world, "sole");
        var before = await StateAsync(world.Scratch);

        await using var db = world.Scratch.NewDb();
        await RunAsync(world, Op.RemoveEntraLink, actor, sole, db);

        (await StateAsync(world.Scratch)).Should().Be(before);
        (await world.AuditRowsAsync(AuditActionNames.IdentityLinkRemoved, "E" + sole)).Should().Be(0);
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastAdminLinkRemoved, "E" + sole)).Should().Be(0);
    }

    // ---- changes that cannot reduce ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A5_changes_that_can_only_raise_viability_are_never_refused_and_take_the_gate(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var stranded = await StrandedAdminAsync(world, "stranded");
        var newcomer = await world.NewUserAsync("newcomer");
        var linkless = await world.NewUserAsync("linkless", Admin);
        await world.Scratch.ExecuteAsync($"DELETE FROM IdentityLinks WHERE UserId = N'{linkless}';");
        var log = new OrderedLog();

        // Enabling the credential of an administrator who could not sign in raises VOA from none to one.
        await using (var db = world.Scratch.NewDb())
        {
            var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
            await Service(world, db, actor, gate: gate).SetLocalCredentialDisabledAsync(new UserId(stranded), disabled: false);
            gate.Acquired.Should().Be(1);
        }

        (await world.CredentialDisabledAsync(stranded)).Should().BeFalse();
        (await world.AuditRowsAsync(AuditActionNames.LocalCredentialReenabled, stranded)).Should().Be(1);

        // Assigning the administrator role to a newcomer.
        await using (var db = world.Scratch.NewDb())
        {
            var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
            await Service(world, db, actor, gate: gate).AssignRoleAsync(new UserId(newcomer), new RoleId(Admin));
            gate.Acquired.Should().Be(1);
        }

        (await world.AssignmentExistsAsync(newcomer, Admin)).Should().BeTrue();

        // Adding a Local link to an administrator who has none.
        await using (var db = world.Scratch.NewDb())
        {
            var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
            await Service(world, db, actor, gate: gate).AddIdentityLinkAsync(new UserId(linkless), IdentitySource.Local, null);
            gate.Acquired.Should().Be(1);
        }

        (await world.LinkCountAsync(linkless, 1)).Should().Be(1);
    }

    // ---- fail closed ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task When_the_configuration_cannot_be_established_a_reduction_is_refused_and_a_raise_is_not(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        var first = await LocalAdminAsync(world, "first");
        var second = await LocalAdminAsync(world, "second");
        var stranded = await StrandedAdminAsync(world, "stranded");
        var before = await StateAsync(world.Scratch);

        foreach (var op in new[] { Op.RemoveRole, Op.DisableCredential })
        {
            await using var db = world.Scratch.NewDb();
            var refusal = await RefusedAsync(() => RunAsync(world, op, actor, first, db, LocalSettingUnknown()));
            refusal.Code.Should().Be("administrator_viability_unavailable");
            refusal.Message.Should().Contain("could not be checked").And.Contain("LocalAuth:Enabled");
        }

        (await StateAsync(world.Scratch)).Should().Be(before, "nothing is changed when the answer cannot be trusted");

        // A change that can only add is not held up by an unreadable setting.
        await using var db2 = world.Scratch.NewDb();
        await Service(world, db2, actor, LocalSettingUnknown()).SetLocalCredentialDisabledAsync(new UserId(stranded), disabled: false);
        (await world.CredentialDisabledAsync(stranded)).Should().BeFalse();
        _ = second;
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task R1_a_running_sign_in_handler_whose_options_cannot_be_read_fails_closed_for_every_reduction_and_never_counts_as_usable(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var actor = await ActorAsync(world, "actor");
        await world.SetActiveRegistrationAsync(Tenant);
        var first = await LocalAdminAsync(world, "first");
        var second = await LocalAdminAsync(world, "second");
        var entraOnly = await EntraOnlyAdminAsync(world, "entra");
        await HealthyRecoveryAsync(world);
        var unreadable = new DeploymentAuthenticationReading(
            ConfigurationTrustLevel.Trusted,
            ConfigurationTrustLevel.Trusted,
            [],
            true,
            new EntraAuthOptions { ClientSecret = "deployment-secret" },
            AuthoritativeAboutSecrets: true,
            new RunningAuthenticationReading(
                new LocalAuthOptions { Enabled = true },
                new EntraAuthOptions { Enabled = true, Authority = "https://login.microsoftonline.com/" + Tenant, ClientId = "client", ClientSecret = "secret" },
                RunningHandlerBinding.Unavailable("InvalidOperationException")));
        var before = await StateAsync(world.Scratch);

        foreach (var op in new[] { Op.RemoveRole, Op.DisableCredential })
        {
            await using var db = world.Scratch.NewDb();
            (await RefusedAsync(() => RunAsync(world, op, actor, first, db, unreadable))).Code.Should().Be("administrator_viability_unavailable");
        }

        await using (var db = world.Scratch.NewDb())
            (await RefusedAsync(() => RunAsync(world, Op.RemoveEntraLink, actor, entraOnly, db, unreadable))).Code.Should().Be("administrator_viability_unavailable");

        (await StateAsync(world.Scratch)).Should().Be(before, "an unreadable handler is neither usable nor unusable, so nothing that could reduce viability is decided on it");

        // The handler never counted: the Entra-only administrator is not viable now, so the state read for a raise is unaffected.
        var state = await ViabilityAsync(world.Scratch, unreadable);
        state.Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([first, second]);
        state.Trust.EntraKnown.Should().BeFalse();
    }

    // ---- authorization is preserved ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Authorization_comes_first_a_non_administrator_cannot_remove_the_role_whatever_the_viability_says(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var roleAssigner = await world.NewRoleAsync("assigner", RoleAssign);
        var nonAdmin = await world.NewUserAsync("non-admin", roleAssigner);
        var target = await LocalAdminAsync(world, "target");
        await LocalAdminAsync(world, "other");

        await using var db = world.Scratch.NewDb();
        var act = () => RunAsync(world, Op.RemoveRole, nonAdmin, target, db);

        (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("platform_administrator_restricted");
        (await world.AssignmentExistsAsync(target, Admin)).Should().BeTrue();
        (await world.AuditRowsAsync(AuditActionNames.BlockedLastPlatformAdminRemoval, target)).Should().Be(0, "an unauthorized request is not a viability refusal");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_actor_without_credential_manage_is_stopped_before_the_gate_is_taken(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        var nobody = await world.NewUserAsync("nobody");
        var target = await LocalAdminAsync(world, "target");
        var log = new OrderedLog();

        await using var db = world.Scratch.NewDb();
        var gate = new LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        var service = world.Service(db, nobody, advisoryAllowsAll: false, gate: gate);
        var act = () => service.SetLocalCredentialDisabledAsync(new UserId(target), disabled: true).AsTask();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        gate.Acquired.Should().Be(0, "the advisory authorization check precedes the unit");
        (await world.CredentialDisabledAsync(target)).Should().BeFalse();
    }

    // ================================================================================================ shared helpers

    /// <summary>Statements and gate acquisitions in the order one connection issued them.</summary>
    internal sealed class OrderedLog
    {
        private readonly List<string> _entries = [];
        private readonly List<int> _outsideTransaction = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_entries)
                    return _entries.ToList();
            }
        }

        /// <summary>The positions of the statements that were issued with no transaction on the command.</summary>
        public IReadOnlyList<int> OutsideTransaction
        {
            get
            {
                lock (_entries)
                    return _outsideTransaction.ToList();
            }
        }

        public void Add(string entry, bool inTransaction = true)
        {
            lock (_entries)
            {
                if (!inTransaction)
                    _outsideTransaction.Add(_entries.Count);
                _entries.Add(entry);
            }
        }
    }

    internal sealed class LoggingStatements(OrderedLog log) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            log.Add(command.CommandText, command.Transaction is not null);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            log.Add(command.CommandText, command.Transaction is not null);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            log.Add(command.CommandText, command.Transaction is not null);
            return ValueTask.FromResult(result);
        }
    }

    internal const string GateEntry = "<<administrator-set gate>>";

    /// <summary>The real gate, noting in the log when it is taken and counting how often.</summary>
    internal sealed class LoggingGate(IPlatformAdministratorSetGate inner, OrderedLog log) : IPlatformAdministratorSetGate
    {
        private int _acquired;

        public int Acquired => Volatile.Read(ref _acquired);

        public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
        {
            log.Add(GateEntry);
            await inner.AcquireAsync(cancellationToken);
            Interlocked.Increment(ref _acquired);
        }
    }

    /// <summary>The gate removed: what a writer that forgot it would do. Only for the control cases that show the gate is what protects.</summary>
    internal sealed class NoGate : IPlatformAdministratorSetGate
    {
        public ValueTask AcquireAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
