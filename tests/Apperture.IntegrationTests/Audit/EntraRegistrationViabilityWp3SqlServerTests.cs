using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Core.Security.Secrets.Models;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using static Apperture.IntegrationTests.Audit.AuditRecorderTestSupport;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2b WP3 against real SQL Server: the Entra registration writers (create, edit, disable, delete, activate and switch) and
/// the sign-in policy writer decide under the global administrator-set gate, on the persisted configuration their own change
/// produced, in the Now and the Restart view (IAM-10 gate §3.2; A10.2b.7, and the parts of A10.2b.1, A10.2b.14, A10.2b.15, A10.2b.16
/// and A10.2b.17 that belong to these writers). Every case runs on a catalog with the default locking READ COMMITTED and on one with
/// READ_COMMITTED_SNAPSHOT. The viability sets are global, so each case starts by clearing the administrator state and the
/// registrations of the shared catalog; the audit table is append-only, so audit rows are read by the ids of the case. Fails,
/// never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed partial class EntraRegistrationViabilityWp3SqlServerTests : IClassFixture<AuditIsolationCatalogs>, IDisposable
{
    internal const string Admin = BootstrapConstants.PlatformAdminRoleId;
    internal const string Tenant = "11111111-1111-1111-1111-111111111111";
    internal const string OtherTenant = "22222222-2222-2222-2222-222222222222";
    internal const string Change = AuditActionNames.TenantRegistrationChangeRefused;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly AuditIsolationCatalogs _catalogs;

    public EntraRegistrationViabilityWp3SqlServerTests(AuditIsolationCatalogs catalogs) => _catalogs = catalogs;

    /// <summary>A failing case must not leave an operation parked with its locks: release every pause the case made.</summary>
    public void Dispose() => PauseAt.ReleaseAll();

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    // ------------------------------------------------------------------------------------------------ the world

    /// <summary>Clears the administrator state and the registrations of the shared catalog, and returns a world over it.</summary>
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
            UPDATE InstanceAuthUiPolicies SET ShowLocalSignInInPrimaryChooser = 1, PreferEntraInChooser = 0, AutoRedirectToEntra = 0;
            """);
        return new RoleAuthorityWorld(scratch);
    }

    private static Task<string> LocalAdminAsync(RoleAuthorityWorld world, string stem) => world.NewUserAsync(stem, Admin);

    /// <summary>An administrator who can sign in only through a proven Entra link: the Local credential is disabled.</summary>
    private static async Task<string> EntraOnlyAdminAsync(RoleAuthorityWorld world, string stem, string tenant = Tenant)
    {
        var id = await world.NewUserAsync(stem, Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{id}';");
        await world.AddEntraLinkAsync(id, tenant, proven: true);
        return id;
    }

    /// <summary>The designated Recovery Access account, which holds the role and has a credential, MFA and a recovery code: healthy.</summary>
    private static async Task<string> HealthyRecoveryAsync(RoleAuthorityWorld world)
    {
        var id = await world.NewUserAsync("recovery", Admin);
        await world.MakeRecoveryAccessHealthyAsync(id);
        return id;
    }

    private static async Task<string> UnhealthyRecoveryAsync(RoleAuthorityWorld world)
    {
        var id = await world.NewUserAsync("recovery", Admin);
        await world.DesignateRecoveryAccessAsync(id);
        return id;
    }

    /// <summary>Seeds a registration row as an older code path or a fixture leaves it (no diagnostics row), and returns its id.</summary>
    private static async Task<string> SeedAsync(
        RoleAuthorityWorld world,
        string tenant,
        bool active,
        bool enabled = true,
        string? secretRef = null,
        bool usesDeploymentSecret = true,
        string? context = null,
        string? name = null,
        string? id = null)
    {
        id ??= "reg" + Guid.NewGuid().ToString("N")[..16];
        await world.Scratch.ExecuteAsync($"""
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, ClientSecretRef, Enabled, IsActive, CreatedUtc, UpdatedUtc,
                 AzureTenantId, AuthenticationContextId, AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (N'{id}', N'{name ?? "Registration " + tenant[..4]}', N'https://login.microsoftonline.com/{tenant}/v2.0', N'client',
                    {(usesDeploymentSecret ? 1 : 0)}, {(secretRef is null ? "NULL" : $"N'{secretRef}'")}, {(enabled ? 1 : 0)}, {(active ? 1 : 0)},
                    SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{tenant}', {(context is null ? "NULL" : $"N'{context}'")}, 0, 0);
            """);
        return id;
    }

    /// <summary>Stores a secret for the registration through the production provisioner (the store's own transaction) and returns its reference.</summary>
    private static async Task<string> StoreSecretAsync(RoleAuthorityWorld world, string registrationId, string secret)
    {
        await using var db = world.Scratch.NewDb();
        return await new EntraClientSecretMaterialProvisioner(EntraRegistrationSqlHarness.SecretStore(db)).StoreNewAsync(registrationId, secret);
    }

    // ------------------------------------------------------------------------------------------------ deployments

    /// <summary>A host that runs no Entra handler we can see: the persisted configuration stands in for the running one. Local sign-in is on.</summary>
    internal static DeploymentAuthenticationReading Deploy(bool local = true) => AdministratorViabilitySqlServerTests.Reading(null, local);

    /// <summary>A host that started with a usable registration for <paramref name="tenant"/>: its endpoints and its handler are bound to it.</summary>
    internal static DeploymentAuthenticationReading Running(string tenant) => AdministratorViabilitySqlServerTests.Reading(tenant);

    /// <summary>A deployment with no client secret of its own, so only a registration's stored secret can make Entra usable.</summary>
    internal static DeploymentAuthenticationReading NoDeploymentSecret(bool local = true) =>
        new(ConfigurationTrustLevel.Trusted, ConfigurationTrustLevel.Trusted, [], local, new EntraAuthOptions(), AuthoritativeAboutSecrets: true, Running: null);

    /// <summary>The running handler's options cannot be read: the Entra reading is unknown.</summary>
    internal static DeploymentAuthenticationReading HandlerUnavailable() =>
        new(
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

    /// <summary>A resolver that fails the way an unreachable secret store does.</summary>
    internal sealed class OutageResolver : ISecretResolver
    {
        public ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default) =>
            throw new IOException("the secret store is unreachable");

        public ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default) =>
            throw new IOException("the secret store is unreachable");
    }

    /// <summary>A resolver that answers the way the SQL resolver does for a reference that names nothing it can serve.</summary>
    internal sealed class MissingSecretResolver : ISecretResolver
    {
        public ValueTask<ResolvedSecret> ResolveAsync(SecretRef secretRef, CancellationToken cancellationToken = default) =>
            throw new SecretNotFoundException();

        public ValueTask<ResolvedSecret> ResolveOwnedAsync(SecretRef secretRef, SecretOwner owner, CancellationToken cancellationToken = default) =>
            throw new SecretUnavailableException();
    }

    // ------------------------------------------------------------------------------------------------ the standard scenario

    /// <summary>
    /// One ordinary administrator, <see cref="A"/>, who can sign in only through Microsoft as the active registration
    /// <see cref="R1"/> provides it (tenant <see cref="Tenant"/>). Nobody else can sign in, so any change that stops
    /// <see cref="A"/> signing in leaves nobody. <see cref="R2"/>, when present, is an inactive registration for
    /// <see cref="OtherTenant"/>.
    /// </summary>
    private sealed class Scenario
    {
        public required RoleAuthorityWorld World { get; init; }
        public required string Actor { get; init; }
        public required string A { get; init; }
        public required string R1 { get; init; }
        public string? R2 { get; init; }
        public DeploymentAuthenticationReading Reading { get; init; } = Deploy();
        public ISecretResolver? Resolver { get; init; }
        public Principal Principal => new(Actor);
        public AuditSqlScratch Scratch => World.Scratch;
    }

    private async Task<Scenario> ReliantAsync(
        bool rcsi,
        DeploymentAuthenticationReading? reading = null,
        ISecretResolver? resolver = null,
        bool withSecondRegistration = false,
        bool recoveryHealthy = true)
    {
        var world = await FreshAsync(rcsi);
        if (recoveryHealthy)
            await HealthyRecoveryAsync(world);
        var a = await EntraOnlyAdminAsync(world, "reliant");
        var actor = await world.NewUserAsync("actor");
        var r1 = await SeedAsync(world, Tenant, active: true);
        var r2 = withSecondRegistration ? await SeedAsync(world, OtherTenant, active: false) : null;
        return new Scenario { World = world, Actor = actor, A = a, R1 = r1, R2 = r2, Reading = reading ?? Deploy(), Resolver = resolver };
    }

    /// <summary>A world with a second administrator who signs in with a Local credential: no registration change can leave nobody.</summary>
    private async Task<Scenario> WithLocalAdministratorAsync(bool rcsi, bool recoveryHealthy = true, bool withSecondRegistration = true)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: withSecondRegistration, recoveryHealthy: recoveryHealthy);
        await LocalAdminAsync(s.World, "local");
        return s;
    }

    private static ConfigEntraTenantsController Tenants(
        Scenario s,
        AppertureDbContext db,
        IPlatformAdministratorSetGate? gate = null,
        ISecretStore? store = null,
        IAuditedUnitOfWork? unit = null,
        IAuditEventWriter? events = null,
        IAuthorizationEvaluationService? authorization = null,
        IDeploymentAuthenticationConfiguration? deployment = null) =>
        EntraRegistrationSqlHarness.Controller(
            s.Scratch,
            db,
            s.Principal,
            authorization: authorization,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: s.Resolver ?? EntraRegistrationSqlHarness.SecretResolver(db), gate: gate, deployment: deployment),
            store: store ?? EntraRegistrationSqlHarness.SecretStore(db),
            unit: unit,
            events: events);

    private static ConfigAuthUiPolicyController PolicyOf(
        Scenario s, AppertureDbContext db, IPlatformAdministratorSetGate? gate = null, IAuthorizationEvaluationService? authorization = null) =>
        EntraRegistrationSqlHarness.PolicyController(
            s.Scratch,
            db,
            s.Principal,
            viability: EntraRegistrationSqlHarness.Viability(db, reading: s.Reading, resolver: EntraRegistrationSqlHarness.SecretResolver(db), gate: gate),
            authorization: authorization);

    // ------------------------------------------------------------------------------------------------ the operations

    public enum RegOp
    {
        /// <summary>Edit the active registration so that it is disabled.</summary>
        Disable,

        /// <summary>Delete the active registration.</summary>
        Delete,

        /// <summary>Make the second registration (another tenant) the active one.</summary>
        Switch,

        /// <summary>Edit the active registration so that it accepts another tenant.</summary>
        ChangeTenant,
    }

    private static string ActionOf(RegOp op) => op switch
    {
        RegOp.Disable or RegOp.ChangeTenant => AuditActionNames.TenantRegistrationUpdated,
        RegOp.Delete => AuditActionNames.TenantRegistrationDeleted,
        _ => AuditActionNames.TenantRegistrationActivated,
    };

    private static string ChangeOf(RegOp op) => op switch
    {
        RegOp.Disable or RegOp.ChangeTenant => "update",
        RegOp.Delete => "delete",
        _ => "activate",
    };

    private static string TargetOf(Scenario s, RegOp op) => op == RegOp.Switch ? s.R2! : s.R1;

    private static async Task<EntraTenantListItemResponse> ItemAsync(Scenario s, string id) =>
        await EntraRegistrationSqlHarness.GetAsync(s.Scratch, id, s.Principal);

    private static UpdateEntraTenantRequestBody Update(EntraTenantListItemResponse item, Action<UpdateEntraTenantRequestBody>? edit = null)
    {
        var body = EntraRegistrationSqlHarness.UpdateBody(item);
        edit?.Invoke(body);
        return body;
    }

    /// <summary>Runs one registration operation on the context it is given and returns the response the controller gave.</summary>
    private static async Task<IActionResult> RunAsync(
        Scenario s,
        RegOp op,
        AppertureDbContext db,
        IPlatformAdministratorSetGate? gate = null,
        IAuditedUnitOfWork? unit = null,
        IAuditEventWriter? events = null,
        IDeploymentAuthenticationConfiguration? deployment = null,
        CancellationToken cancellationToken = default)
    {
        var controller = Tenants(s, db, gate, unit: unit, events: events, deployment: deployment);
        switch (op)
        {
            case RegOp.Disable:
            {
                var item = await ItemAsync(s, s.R1);
                return (await controller.Patch(s.R1, Update(item, b => b.Enabled = false), cancellationToken)).Result!;
            }

            case RegOp.ChangeTenant:
            {
                var item = await ItemAsync(s, s.R1);
                return (await controller.Patch(
                    s.R1,
                    Update(item, b =>
                    {
                        b.Authority = $"https://login.microsoftonline.com/{OtherTenant}/v2.0";
                        b.AzureTenantId = OtherTenant;
                    }),
                    cancellationToken)).Result!;
            }

            case RegOp.Delete:
                return await controller.Delete(s.R1, cancellationToken);

            default:
                return await controller.Activate(s.R2!, cancellationToken);
        }
    }

    private static string? CodeOf(IActionResult result)
    {
        if (result is not ObjectResult { Value: { } value })
            return null;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string? MessageOf(IActionResult result)
    {
        if (result is not ObjectResult { Value: { } value })
            return null;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("message", out var message) ? message.GetString() : null;
    }

    private static bool Succeeded(IActionResult result) => result is OkObjectResult or NoContentResult or CreatedAtActionResult;

    // ------------------------------------------------------------------------------------------------ reading state

    /// <summary>A stable text of every row a registration writer or an administrator writer here can change.</summary>
    private static Task<string> StateAsync(AuditSqlScratch scratch) =>
        scratch.ScalarAsync<string>("""
            SELECT CONCAT(
                (SELECT STRING_AGG(CAST(CONCAT(UserId, '/', RoleId) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY UserId, RoleId) FROM UserRoles), '|',
                (SELECT STRING_AGG(CAST(CONCAT(UserId, ':', IsDisabled) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY UserId) FROM LocalCredentials), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id, ':', UserId, ':', Source, ':', CASE WHEN LastAuthenticatedAt IS NULL THEN 0 ELSE 1 END) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM IdentityLinks), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id, ':', IsBreakGlassEligible) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM Users), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id, ':', DisplayName, ':', Authority, ':', ClientId, ':', UsesDeploymentSecret, ':', ISNULL(ClientSecretRef, ''), ':', Enabled, ':', IsActive, ':', ISNULL(AzureTenantId, ''), ':', ISNULL(AuthenticationContextId, ''), ':', ISNULL(CallbackPath, ''), ':', AllowMemberAccessRequests, ':', AllowGuestAccessRequests) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM EntraInstanceRegistrations), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id, ':', State) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM SecretDescriptors), '|',
                (SELECT COUNT(*) FROM SecretVersions), '|',
                (SELECT STRING_AGG(CAST(CONCAT(Id, ':', ShowLocalSignInInPrimaryChooser, ':', PreferEntraInChooser, ':', AutoRedirectToEntra) AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY Id) FROM InstanceAuthUiPolicies));
            """);

    /// <summary>The viability state as the host reads it, for the configuration the case runs under.</summary>
    private static async Task<AdministratorViabilityState> ViabilityAsync(AuditSqlScratch scratch, DeploymentAuthenticationReading? reading = null, ISecretResolver? resolver = null)
    {
        await using var db = scratch.NewDb();
        return await AdministratorViabilitySqlServerTests.Service(db, reading ?? Deploy(), resolver ?? EntraRegistrationSqlHarness.SecretResolver(db)).ReadAsync();
    }

    private static Task<int> RefusalRowsAsync(AuditSqlScratch scratch, string registrationId) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{Change}' AND TargetId = N'{registrationId}';");

    private static Task<int> RowsAsync(AuditSqlScratch scratch, string action, string registrationId) =>
        scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{action}' AND TargetId = N'{registrationId}';");

    private static Task<int> SecretDescriptorsAsync(AuditSqlScratch scratch) => scratch.ScalarAsync<int>("SELECT COUNT(*) FROM SecretDescriptors;");

    private static async Task<IReadOnlyList<string>> RefusalDetailsAsync(AuditSqlScratch scratch, string registrationId)
    {
        var rows = await scratch.RowsAsync($"SELECT Details FROM AuditLogs WHERE Action = N'{Change}' AND TargetId = N'{registrationId}' ORDER BY Id;");
        return rows.Select(r =>
        {
            using var document = JsonDocument.Parse((string)r["Details"]!);
            return document.RootElement.GetProperty("change").GetString() + "/" + document.RootElement.GetProperty("reason").GetString();
        }).ToList();
    }

    // ================================================================================================ the refusals

    public static IEnumerable<object[]> ReducingOpsOnBothModes() =>
        from op in Enum.GetValues<RegOp>()
        from rcsi in new[] { false, true }
        select new object[] { op, rcsi };

    [Theory]
    [MemberData(nameof(ReducingOpsOnBothModes))]
    public async Task A7_a_change_that_stops_the_last_viable_administrator_signing_in_is_refused_with_a_typed_409_and_changes_nothing(RegOp op, bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var target = TargetOf(s, op);
        (await ViabilityAsync(s.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A], "the administrator signs in through the active registration");
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var response = await RunAsync(s, op, db);

        response.Should().BeOfType<ConflictObjectResult>();
        CodeOf(response).Should().Be("last_viable_sign_in_method");
        MessageOf(response).Should().NotBeNullOrWhiteSpace().And.Contain("sign in");
        (await StateAsync(s.Scratch)).Should().Be(before, "the change was applied in the unit's transaction and rolled back with everything it did");
        (await RowsAsync(s.Scratch, ActionOf(op), target)).Should().Be(0);
        (await RefusalDetailsAsync(s.Scratch, target)).Should().Equal(new[] { ChangeOf(op) + "/sign_in_lockout" }, "the existing refusal row of the operation, written once after the rollback");
        (await ViabilityAsync(s.Scratch)).Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
    }

    [Theory]
    [MemberData(nameof(ReducingOpsOnBothModes))]
    public async Task A7_the_same_change_is_allowed_when_another_administrator_signs_in_locally(RegOp op, bool rcsi)
    {
        var s = await WithLocalAdministratorAsync(rcsi);
        var target = TargetOf(s, op);

        await using var db = s.Scratch.NewDb();
        var response = await RunAsync(s, op, db);

        Succeeded(response).Should().BeTrue($"{op} cannot leave nobody while a Local administrator remains: {CodeOf(response)}");
        (await RowsAsync(s.Scratch, ActionOf(op), target)).Should().Be(1);
        (await RefusalRowsAsync(s.Scratch, target)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(ReducingOpsOnBothModes))]
    public async Task Recovery_Access_is_never_the_administrator_a_registration_change_must_leave(RegOp op, bool rcsi)
    {
        // Recovery Access is healthy and holds the role, but it is not an ordinary administrator: only A counts.
        var s = await ReliantAsync(rcsi, withSecondRegistration: true, recoveryHealthy: true);
        (await ViabilityAsync(s.Scratch)).RecoveryHealthy.Should().BeTrue();

        await using var db = s.Scratch.NewDb();
        var response = await RunAsync(s, op, db);

        CodeOf(response).Should().Be("last_viable_sign_in_method");
    }

    [Theory]
    [MemberData(nameof(ReducingOpsOnBothModes))]
    public async Task A8_a_registration_change_never_removes_the_entra_independent_fallback(RegOp op, bool rcsi)
    {
        // Recovery Access is not healthy and one Local administrator is the only Entra-independent one. A registration change
        // cannot touch that administrator's Local sign-in, so the fallback invariant is never what refuses it.
        var s = await WithLocalAdministratorAsync(rcsi, recoveryHealthy: false);
        (await ViabilityAsync(s.Scratch)).RecoveryHealthy.Should().BeFalse();

        await using var db = s.Scratch.NewDb();
        var response = await RunAsync(s, op, db);

        Succeeded(response).Should().BeTrue();
        CodeOf(response).Should().NotBe("entra_independent_fallback_required");
    }

    // ================================================================================================ the switch itself

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Switching_the_active_registration_works_whichever_registration_id_sorts_first(bool rcsi)
    {
        // The store allows one active registration (a filtered unique index) and writes the rows of one save in key order, so a
        // target that sorts before the registration it replaces used to be active beside it for the length of the batch.
        var world = await FreshAsync(rcsi);
        var low = await SeedAsync(world, Tenant, active: true, id: "aaaa-sorts-first");
        var high = await SeedAsync(world, OtherTenant, active: false, id: "zzzz-sorts-last");

        await using (var db = world.Scratch.NewDb())
            await new SqlEntraInstanceRegistrationRepository(db).SetActiveAsync(high);
        (await world.Scratch.ScalarAsync<string>("SELECT STRING_AGG(Id, ',') WITHIN GROUP (ORDER BY Id) FROM EntraInstanceRegistrations WHERE IsActive = 1;")).Should().Be(high);

        await using (var db = world.Scratch.NewDb())
            await new SqlEntraInstanceRegistrationRepository(db).SetActiveAsync(low);
        (await world.Scratch.ScalarAsync<string>("SELECT STRING_AGG(Id, ',') WITHIN GROUP (ORDER BY Id) FROM EntraInstanceRegistrations WHERE IsActive = 1;")).Should().Be(low);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Switching_through_the_controller_works_in_both_key_orders_and_bumps_every_registrations_row_version_as_before(bool rcsi)
    {
        var world = await FreshAsync(rcsi);
        await world.NewUserAsync("local", Admin);
        var actor = await world.NewUserAsync("actor");
        var low = await SeedAsync(world, Tenant, active: false, id: "aaaa-sorts-first");
        var high = await SeedAsync(world, OtherTenant, active: true, id: "zzzz-sorts-last");
        var s = new Scenario { World = world, Actor = actor, A = actor, R1 = high, R2 = low };
        var rowVersionBefore = await world.Scratch.ScalarAsync<string>($"SELECT CONVERT(varchar(32), RowVersion, 2) FROM EntraInstanceRegistrations WHERE Id = N'{high}';");

        await using (var db = world.Scratch.NewDb())
            (await Tenants(s, db).Activate(low, CancellationToken.None)).Should().BeOfType<NoContentResult>("the target sorts before the registration it replaces");

        (await world.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{low}';")).Should().BeTrue();
        (await world.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{high}';")).Should().BeFalse();
        (await world.Scratch.ScalarAsync<string>($"SELECT CONVERT(varchar(32), RowVersion, 2) FROM EntraInstanceRegistrations WHERE Id = N'{high}';")).Should().NotBe(rowVersionBefore);
        (await RowsAsync(world.Scratch, AuditActionNames.TenantRegistrationActivated, low)).Should().Be(1);
    }

    // ================================================================================================ the shape of the change

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A7_a_same_tenant_edit_of_the_active_registration_is_allowed_for_the_last_viable_administrator(bool rcsi)
    {
        var s = await ReliantAsync(rcsi);
        var item = await ItemAsync(s, s.R1);

        await using var db = s.Scratch.NewDb();
        var response = (await Tenants(s, db).Patch(s.R1, Update(item, b => b.DisplayName = "Renamed"), CancellationToken.None)).Result!;

        response.Should().BeOfType<OkObjectResult>();
        (await ViabilityAsync(s.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
        (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationUpdated, s.R1)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Changing_or_deleting_an_inactive_registration_cannot_reduce_anything_and_is_allowed(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var item = await ItemAsync(s, s.R2!);

        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Patch(s.R2!, Update(item, b => b.Enabled = false), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Delete(s.R2!, CancellationToken.None)).Should().BeOfType<NoContentResult>();

        (await s.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{s.R2}';")).Should().Be(0);
        (await ViabilityAsync(s.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Switching_to_a_registration_for_a_tenant_the_administrator_is_also_proven_for_is_allowed(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        await s.World.Scratch.ExecuteAsync($"""
            INSERT INTO IdentityLinks (Id, UserId, Source, ExternalId, TenantId, LinkedAt, LastAuthenticatedAt)
            VALUES (N'E2{s.A}', N'{s.A}', 0, N'oid2-{s.A}', N'{OtherTenant}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        await using var db = s.Scratch.NewDb();
        var response = await RunAsync(s, RegOp.Switch, db);

        response.Should().BeOfType<NoContentResult>();
        var state = await ViabilityAsync(s.Scratch);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A], "the administrator is proven for the tenant the switch accepts");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Activating_a_registration_whose_secret_exists_only_through_its_stored_reference_raises_viability_and_is_allowed(bool rcsi)
    {
        // No deployment secret exists: only the registration's own stored secret can make Microsoft sign-in usable.
        var s = await ReliantAsync(rcsi, reading: NoDeploymentSecret());
        await s.World.Scratch.ExecuteAsync($"DELETE FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';");
        var inactive = await SeedAsync(s.World, Tenant, active: false, usesDeploymentSecret: false);
        var secretRef = await StoreSecretAsync(s.World, inactive, "stored-only-secret");
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secretRef}' WHERE Id = N'{inactive}';");
        (await ViabilityAsync(s.Scratch, s.Reading)).Restart.ViableOrdinaryAdministrators.Should().BeEmpty("nothing is active yet");

        await using var db = s.Scratch.NewDb();
        var response = await Tenants(s, db).Activate(inactive, CancellationToken.None);

        response.Should().BeOfType<NoContentResult>();
        var state = await ViabilityAsync(s.Scratch, s.Reading);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A], "the stored secret makes Microsoft sign-in usable, which the old simulation (deployment secret only) could not see");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Editing_a_registration_that_holds_only_a_stored_secret_is_judged_on_that_secret_not_on_the_deployment_one(bool rcsi)
    {
        // The registration's stored secret is what the host runs; the deployment has none. The old simulation read only the deployment
        // secret and would have called every edit of such a registration a lockout.
        var s = await ReliantAsync(rcsi, reading: NoDeploymentSecret());
        await s.World.Scratch.ExecuteAsync($"DELETE FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';");
        var active = await SeedAsync(s.World, Tenant, active: true, usesDeploymentSecret: false);
        var secretRef = await StoreSecretAsync(s.World, active, "stored-only-secret");
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secretRef}' WHERE Id = N'{active}';");
        (await ViabilityAsync(s.Scratch, s.Reading)).Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
        var item = await ItemAsync(s, active);

        // The API only accepts a registration that uses the deployment secret, so saving this one sets that flag; the stored secret
        // still comes first, and the deployment has none to fall back on.
        await using var db = s.Scratch.NewDb();
        var response = (await Tenants(s, db).Patch(active, Update(item, b => { b.DisplayName = "Renamed"; b.UsesDeploymentSecret = true; }), CancellationToken.None)).Result!;

        response.Should().BeOfType<OkObjectResult>();
        (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationUpdated, active)).Should().Be(1);
        (await ViabilityAsync(s.Scratch, s.Reading)).Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
    }

    // ================================================================================================ invalid and absent configuration

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_authentication_context_id_the_api_rejects_is_a_400_and_changes_nothing(bool rcsi)
    {
        var s = await ReliantAsync(rcsi);
        var item = await ItemAsync(s, s.R1);
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var response = (await Tenants(s, db).Patch(s.R1, Update(item, b => b.AuthenticationContextId = "not-a-context"), CancellationToken.None)).Result!;

        response.Should().BeOfType<BadRequestObjectResult>();
        (await StateAsync(s.Scratch)).Should().Be(before);
        (await RefusalRowsAsync(s.Scratch, s.R1)).Should().Be(0, "a malformed request is not a recorded refusal");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_stored_invalid_authentication_context_makes_a_registration_unusable_so_activating_it_would_leave_nobody_and_fixing_it_is_a_raise(bool rcsi)
    {
        var s = await ReliantAsync(rcsi);
        var invalid = await SeedAsync(s.World, OtherTenant, active: false, context: "bogus");

        // Activating it would switch away from the usable registration to one the sign-in cannot use (the merged snapshot rejects it).
        await using (var db = s.Scratch.NewDb())
        {
            var response = await Tenants(s, db).Activate(invalid, CancellationToken.None);
            CodeOf(response).Should().Be("last_viable_sign_in_method");
        }

        (await s.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{invalid}';")).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_registration_that_is_already_unusable_does_not_stop_an_unrelated_or_repairing_change(bool rcsi)
    {
        // The active registration has an invalid stored context, so nobody can sign in through it: I-VIABLE already does not hold.
        var s = await ReliantAsync(rcsi);
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET AuthenticationContextId = N'bogus' WHERE Id = N'{s.R1}';");
        (await ViabilityAsync(s.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEmpty();

        await using (var db = s.Scratch.NewDb())
        {
            var item = await ItemAsync(s, s.R1);
            (await Tenants(s, db).Patch(s.R1, Update(item, b => { b.AuthenticationContextId = "c1"; b.DisplayName = "Repaired"; }), CancellationToken.None)).Result
                .Should().BeOfType<OkObjectResult>("repairing the registration only raises viability");
        }

        (await ViabilityAsync(s.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A]);
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Delete(s.R1, CancellationToken.None)).Should().BeOfType<ConflictObjectResult>("now the registration is the administrator's only way in");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task With_no_active_registration_nobody_relies_on_Microsoft_so_changes_to_the_others_are_allowed(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET IsActive = 0 WHERE Id = N'{s.R1}';");
        (await ViabilityAsync(s.Scratch)).Now.ViableOrdinaryAdministrators.Should().BeEmpty("no registration is active, so no Microsoft token is accepted");
        var item = await ItemAsync(s, s.R2!);

        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Patch(s.R2!, Update(item, b => b.DisplayName = "Edited"), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Delete(s.R1, CancellationToken.None)).Should().BeOfType<NoContentResult>();
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task No_configured_entra_secret_means_microsoft_sign_in_is_unusable_and_changes_are_judged_on_that(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, reading: NoDeploymentSecret());
        (await ViabilityAsync(s.Scratch, s.Reading)).Restart.ViableOrdinaryAdministrators.Should().BeEmpty("no secret, so Microsoft sign-in is unusable and the administrator is not viable");

        await using var db = s.Scratch.NewDb();
        (await Tenants(s, db).Delete(s.R1, CancellationToken.None)).Should().BeOfType<NoContentResult>("an unusable registration is not what the administrator signs in with");
    }

    // ================================================================================================ secret uncertainty

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_secret_store_outage_is_unknown_and_a_reducing_change_fails_closed_without_a_misleading_refusal(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, resolver: new OutageResolver());
        var secretRef = await StoreSecretAsync(s.World, s.R1, "stored");
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secretRef}' WHERE Id = N'{s.R1}';");
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var response = await Tenants(s, db).Delete(s.R1, CancellationToken.None);

        response.Should().BeOfType<ConflictObjectResult>();
        CodeOf(response).Should().Be("administrator_viability_unavailable");
        MessageOf(response).Should().NotContain(secretRef).And.NotContain("unreachable", "the resolver's own error text is not disclosed");
        (await StateAsync(s.Scratch)).Should().Be(before);
        (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationDeleted, s.R1)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_secret_store_outage_does_not_stop_a_change_that_leaves_the_effective_configuration_as_it_was(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, resolver: new OutageResolver(), withSecondRegistration: true);
        var secretRef = await StoreSecretAsync(s.World, s.R1, "stored");
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secretRef}' WHERE Id = N'{s.R1}';");
        var item = await ItemAsync(s, s.R1);

        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Patch(s.R1, Update(item, b => b.DisplayName = "Renamed during an outage"), CancellationToken.None)).Result
                .Should().BeOfType<OkObjectResult>("renaming changes nothing the sign-in reads");
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Delete(s.R2!, CancellationToken.None)).Should().BeOfType<NoContentResult>("an inactive registration is not part of the effective configuration");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_secret_the_store_confirms_missing_is_a_known_absence_and_not_an_outage(bool rcsi)
    {
        // The reference names nothing the store can serve and no deployment secret exists: Microsoft sign-in is known to be unusable.
        var s = await ReliantAsync(rcsi, reading: NoDeploymentSecret(), resolver: new MissingSecretResolver());
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{Guid.NewGuid():D}', UsesDeploymentSecret = 0 WHERE Id = N'{s.R1}';");
        var state = await ViabilityAsync(s.Scratch, s.Reading, s.Resolver);
        state.Trust.Entra.Should().Be(ConfigurationTrustLevel.Trusted);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEmpty();

        await using var db = s.Scratch.NewDb();
        var response = await Tenants(s, db).Delete(s.R1, CancellationToken.None);

        response.Should().BeOfType<NoContentResult>("the absence is established, so the registration is not what anyone signs in with");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_legacy_deployment_secret_fallback_is_read_as_the_host_applies_it_so_the_registration_still_counts(bool rcsi)
    {
        // A registration that neither asks for the deployment secret nor holds one still runs on it, as the host has always done.
        var s = await ReliantAsync(rcsi);
        await s.World.Scratch.ExecuteAsync($"UPDATE EntraInstanceRegistrations SET UsesDeploymentSecret = 0 WHERE Id = N'{s.R1}';");
        var state = await ViabilityAsync(s.Scratch);
        state.Restart.ViableOrdinaryAdministrators.Should().BeEquivalentTo([s.A], "the viability reading agrees with what the host runs");
        state.Configuration.Restart.Notes.Should().Contain(n => n.Contains("does not ask for"));

        await using var db = s.Scratch.NewDb();
        var response = await Tenants(s, db).Delete(s.R1, CancellationToken.None);

        CodeOf(response).Should().Be("last_viable_sign_in_method");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_api_still_refuses_a_registration_that_does_not_use_the_deployment_secret(bool rcsi)
    {
        var s = await ReliantAsync(rcsi);
        var item = await ItemAsync(s, s.R1);
        var before = await StateAsync(s.Scratch);

        await using var db = s.Scratch.NewDb();
        var response = (await Tenants(s, db).Patch(s.R1, Update(item, b => b.UsesDeploymentSecret = false), CancellationToken.None)).Result!;

        response.Should().BeOfType<BadRequestObjectResult>("the field validation is unchanged");
        (await StateAsync(s.Scratch)).Should().Be(before);
    }

    // ================================================================================================ unknown configuration

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Unknown_configuration_fails_closed_for_a_change_that_can_reduce_and_not_for_one_that_cannot(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, reading: HandlerUnavailable(), withSecondRegistration: true);
        var item = await ItemAsync(s, s.R1);
        var before = await StateAsync(s.Scratch);

        await using (var db = s.Scratch.NewDb())
            CodeOf((await Tenants(s, db).Patch(s.R1, Update(item, b => b.Enabled = false), CancellationToken.None)).Result!).Should().Be("administrator_viability_unavailable");
        (await StateAsync(s.Scratch)).Should().Be(before);

        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Patch(s.R1, Update(item, b => b.DisplayName = "Renamed"), CancellationToken.None)).Result
                .Should().BeOfType<OkObjectResult>("the effective configuration is exactly what it was");
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Delete(s.R2!, CancellationToken.None)).Should().BeOfType<NoContentResult>();
    }

    // ================================================================================================ no-ops

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_registration_no_op_is_silent_for_the_last_viable_administrator_and_when_the_system_is_already_unhealthy(bool rcsi)
    {
        var s = await ReliantAsync(rcsi, withSecondRegistration: true);
        var item = await ItemAsync(s, s.R1);
        var before = await StateAsync(s.Scratch);
        var auditBefore = await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Patch(s.R1, Update(item), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>("the same settings answer as before");
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Activate(s.R1, CancellationToken.None)).Should().BeOfType<NoContentResult>("already active");
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Delete("no-such-registration", CancellationToken.None)).Should().BeOfType<NoContentResult>("nothing to delete");

        (await StateAsync(s.Scratch)).Should().Be(before);
        (await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore, "a no-op is neither a change nor a recorded refusal");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_registration_no_op_is_not_refused_merely_because_the_system_is_already_unhealthy(bool rcsi)
    {
        // The deployment has no secret, so Microsoft sign-in is unusable and nobody can sign in: I-VIABLE already does not hold.
        var s = await ReliantAsync(rcsi, reading: NoDeploymentSecret(), withSecondRegistration: true);
        (await ViabilityAsync(s.Scratch, s.Reading)).Now.ViableOrdinaryAdministrators.Should().BeEmpty();
        var item = await ItemAsync(s, s.R1);
        var before = await StateAsync(s.Scratch);
        var auditBefore = await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;");

        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Patch(s.R1, Update(item), CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
        await using (var db = s.Scratch.NewDb())
            (await Tenants(s, db).Activate(s.R1, CancellationToken.None)).Should().BeOfType<NoContentResult>();

        (await StateAsync(s.Scratch)).Should().Be(before);
        (await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs;")).Should().Be(auditBefore);
        (await RefusalRowsAsync(s.Scratch, s.R1)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Unknown_registrations_are_404_and_an_unauthorized_actor_is_forbidden_before_the_gate(bool rcsi)
    {
        var s = await ReliantAsync(rcsi);
        var item = await ItemAsync(s, s.R1);
        var log = new AdministratorViabilityWp2SqlServerTests.OrderedLog();

        await using var db = s.Scratch.NewDb();
        var gate = new AdministratorViabilityWp2SqlServerTests.LoggingGate(new SqlPlatformAdministratorSetGate(db), log);
        (await Tenants(s, db, gate).Patch("missing", Update(item), CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
        (await Tenants(s, db, gate).Activate("missing", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        (await Tenants(s, db, gate, authorization: new EntraRegistrationSqlHarness.Allow()).Delete(s.R1, CancellationToken.None)).Should().BeOfType<ForbidResult>();

        (await RowsAsync(s.Scratch, AuditActionNames.TenantRegistrationDeleted, s.R1)).Should().Be(0);
        gate.Acquired.Should().Be(1, "the edit's lookup answers 404 before any unit; the activation's lookup is inside its unit, under the gate; the forbidden request never reached a unit");
    }

    // ================================================================================================ create

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Creating_a_registration_is_raise_only_it_reads_no_viability_state_and_is_never_refused_by_the_rest_of_the_system(bool rcsi)
    {
        // Nobody is viable, Recovery Access is not healthy, and the running handler cannot be read: none of that is the writer's doing.
        var s = await ReliantAsync(rcsi, reading: HandlerUnavailable(), recoveryHealthy: false);
        var deployment = new EntraRegistrationSqlHarness.FixedDeployment(HandlerUnavailable());
        await s.World.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1;");

        await using var db = s.Scratch.NewDb();
        var created = await Tenants(s, db, deployment: deployment).Create(EntraRegistrationSqlHarness.CreateBody("New"), CancellationToken.None);

        created.Result.Should().BeOfType<CreatedAtActionResult>();
        deployment.Initializations.Should().Be(1, "the running options are built once before the unit opens");
        deployment.Reads.Should().Be(0, "the creation never reads the configuration or the administrators: it can only add an inactive registration");
        (await s.Scratch.ScalarAsync<bool>("SELECT IsActive FROM EntraInstanceRegistrations WHERE DisplayName = N'New';")).Should().BeFalse();
        (await s.Scratch.ScalarAsync<bool>($"SELECT IsActive FROM EntraInstanceRegistrations WHERE Id = N'{s.R1}';")).Should().BeTrue("the active registration is untouched");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Creating_a_registration_with_a_secret_stores_it_with_the_registration_and_a_busy_gate_stores_nothing(bool rcsi)
    {
        var s = await ReliantAsync(rcsi);
        var descriptorsBefore = await SecretDescriptorsAsync(s.Scratch);
        var body = EntraRegistrationSqlHarness.CreateBody("With a secret");
        body.ClientSecret = "brand-new-secret";

        await using (var holder = await RawSession.BeginAsync(s.Scratch.ConnectionString))
        {
            await holder.ExecuteAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;");
            await using var db = s.Scratch.NewDb();
            var busy = await Tenants(s, db, new SqlPlatformAdministratorSetGate(db, TimeSpan.FromMilliseconds(300))).Create(body, CancellationToken.None);

            CodeOf(busy.Result!).Should().Be("governance_operation_busy");
            (busy.Result as ConflictObjectResult)!.Value!.ToString().Should().Contain("retry = True");
            (await SecretDescriptorsAsync(s.Scratch)).Should().Be(descriptorsBefore, "the gate is taken before the secret is stored");
            (await s.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE DisplayName = N'With a secret';")).Should().Be(0);
            await holder.RollbackAsync();
        }

        await using var retry = s.Scratch.NewDb();
        (await Tenants(s, retry).Create(body, CancellationToken.None)).Result.Should().BeOfType<CreatedAtActionResult>("the busy result was transient");
        (await SecretDescriptorsAsync(s.Scratch)).Should().Be(descriptorsBefore + 1);
    }
}
