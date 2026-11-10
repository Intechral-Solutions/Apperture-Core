using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.ControlPlane.Security.AdministratorViability;
using Apperture.Core.Application.AdministratorViability;
using Apperture.IntegrationTests.Audit;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Apperture.Management.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.ManagementHost;

using HostFactory = WebApplicationFactory<ManagementHostWebApplicationFactoryEntryPoint>;

/// <summary>
/// IAM-10.2b WP2 at the real Management Host boundary (A10.2b.1 to A10.2b.6, A10.2b.15): the stable codes, the structured bodies and
/// the statuses of the viability refusals for the role, credential and identity-link endpoints, a busy gate, and the absence of an
/// unhandled exception or a 404 where the answer is a refusal. One host per isolation mode with Local sign-in only, one with a usable
/// Entra registration (so an Entra link can be the last way in), each on a throwaway catalog. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class AdministratorViabilityHostHttpTests : IClassFixture<AdministratorViabilityHostHttpTests.Hosts>
{
    private const string Admin = "role-platform-administrator";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private readonly Hosts _hosts;

    public AdministratorViabilityHostHttpTests(Hosts hosts) => _hosts = hosts;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    public sealed class Hosts : IAsyncLifetime
    {
        public AuditSqlScratch Locking { get; private set; } = null!;
        public AuditSqlScratch Rcsi { get; private set; } = null!;
        public AuditSqlScratch EntraLocking { get; private set; } = null!;
        public AuditSqlScratch EntraRcsi { get; private set; } = null!;
        public HostFactory LockingHost { get; private set; } = null!;
        public HostFactory RcsiHost { get; private set; } = null!;
        public HostFactory EntraLockingHost { get; private set; } = null!;
        public HostFactory EntraRcsiHost { get; private set; } = null!;
        private readonly List<IAsyncDisposable> _extra = [];

        public async Task InitializeAsync()
        {
            Locking = await AuditIsolation.CreateAsync("Apperture_VbHost", rcsi: false);
            Rcsi = await AuditIsolation.CreateAsync("Apperture_VbHost", rcsi: true);
            EntraLocking = await AuditIsolation.CreateAsync("Apperture_VbEntra", rcsi: false);
            EntraRcsi = await AuditIsolation.CreateAsync("Apperture_VbEntra", rcsi: true);
            LockingHost = await StartAsync(Locking, entra: false);
            RcsiHost = await StartAsync(Rcsi, entra: false);
            EntraLockingHost = await StartAsync(EntraLocking, entra: true);
            EntraRcsiHost = await StartAsync(EntraRcsi, entra: true);
        }

        private static async Task<HostFactory> StartAsync(AuditSqlScratch scratch, bool entra)
        {
            HostFactory factory = new ManagementHostWebApplicationFactory(scratch.ConnectionString);
            if (entra)
            {
                // The registration is in the catalog before the host starts, and it uses the deployment's client secret, so the
                // host starts with Entra sign-in enabled and its handler bound to this tenant.
                await new RoleAuthorityWorld(scratch).SetActiveRegistrationAsync(Tenant);
                factory = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { ["Entra:ClientSecret"] = "host-test-secret" })));
            }

            using var client = factory.CreateClient();
            _ = await client.GetAsync("/signin"); // starts the host, which migrates, registers the catalog and repairs the administrator role
            return factory;
        }

        /// <summary>A host on the same catalog whose gate gives up after a few hundred milliseconds, so that a busy answer does not take 15 seconds.</summary>
        public HttpClient ImpatientClient(HostFactory host, string username)
        {
            var impatient = host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPlatformAdministratorSetGate>();
                services.AddScoped<IPlatformAdministratorSetGate>(sp =>
                    new SqlPlatformAdministratorSetGate(sp.GetRequiredService<AppertureDbContext>(), TimeSpan.FromMilliseconds(300)));
            }));
            _extra.Add(impatient);
            var client = impatient.CreateClient();
            client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, username);
            return client;
        }

        private sealed class UnreadableHandler : IRunningEntraHandlerBindingSource
        {
            public RunningHandlerBinding Read() => RunningHandlerBinding.Unavailable("InvalidOperationException");
        }

        /// <summary>A host on the same catalog whose sign-in handler's options cannot be read (the Entra reading is unknown).</summary>
        public HttpClient UnreadableHandlerClient(HostFactory host, string username)
        {
            var unreadable = host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRunningEntraHandlerBindingSource>();
                services.AddSingleton<IRunningEntraHandlerBindingSource, UnreadableHandler>();
            }));
            _extra.Add(unreadable);
            var client = unreadable.CreateClient();
            client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, username);
            return client;
        }


        public async Task DisposeAsync()
        {
            foreach (var extra in _extra)
                await extra.DisposeAsync();
            await LockingHost.DisposeAsync();
            await RcsiHost.DisposeAsync();
            await EntraLockingHost.DisposeAsync();
            await EntraRcsiHost.DisposeAsync();
            await Locking.DisposeAsync();
            await Rcsi.DisposeAsync();
            await EntraLocking.DisposeAsync();
            await EntraRcsi.DisposeAsync();
        }
    }

    private (RoleAuthorityWorld World, HostFactory Host) For(bool rcsi, bool entra = false) =>
        (entra, rcsi) switch
        {
            (false, false) => (new RoleAuthorityWorld(_hosts.Locking), _hosts.LockingHost),
            (false, true) => (new RoleAuthorityWorld(_hosts.Rcsi), _hosts.RcsiHost),
            (true, false) => (new RoleAuthorityWorld(_hosts.EntraLocking), _hosts.EntraLockingHost),
            _ => (new RoleAuthorityWorld(_hosts.EntraRcsi), _hosts.EntraRcsiHost),
        };

    private static async Task ClearAdministratorsAsync(RoleAuthorityWorld world) =>
        await world.Scratch.ExecuteAsync($"DELETE FROM UserRoles WHERE RoleId = N'{Admin}'; UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;");

    private static HttpClient As(HostFactory host, string username)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, username);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>An administrator who holds the role and so may act, but cannot sign in (credential disabled) and so counts for nothing.</summary>
    private static async Task<RoleAuthorityWorld.Person> StrandedActorAsync(RoleAuthorityWorld world)
    {
        var person = await world.NewPersonAsync("actor", Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{person.Id}';");
        return person;
    }

    private static async Task AssertRefusalAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a refusal is a 409, never a 404 or an unhandled 500");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await BodyAsync(response);
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace("the refusal says what to do");
        body.TryGetProperty("retry", out _).Should().BeFalse("a business refusal is not a transient conflict");
    }

    // ---------------------------------------------------------------------------------------------------- roles

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_the_role_of_the_last_administrator_who_can_sign_in_is_409_even_though_two_hold_it(bool rcsi)
    {
        var (world, host) = For(rcsi);
        await ClearAdministratorsAsync(world);
        var actor = await StrandedActorAsync(world);
        var viable = await world.NewPersonAsync("viable", Admin);
        using var client = As(host, actor.Username);

        var refused = await client.DeleteAsync($"api/users/{viable.Id}/roles/{Admin}");

        await AssertRefusalAsync(refused, "last_viable_administrator");
        (await world.AssignmentExistsAsync(viable.Id, Admin)).Should().BeTrue();
        (await world.AuditRowsAsync("auth_admin.blocked.last_platform_admin_removal", viable.Id)).Should().Be(1);

        // The administrator who cannot sign in can go, because one who can remains.
        var allowed = await client.DeleteAsync($"api/users/{actor.Id}/roles/{Admin}");
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.AssignmentExistsAsync(actor.Id, Admin)).Should().BeFalse();
    }

    // ------------------------------------------------------------------------------------------------ credential

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Disabling_the_last_working_local_credential_is_409_last_local_credential_and_a_repeat_on_a_disabled_one_is_a_204_no_op(bool rcsi)
    {
        var (world, host) = For(rcsi);
        await ClearAdministratorsAsync(world);
        var actor = await StrandedActorAsync(world);
        var sole = await world.NewPersonAsync("sole", Admin);
        using var client = As(host, actor.Username);

        var refused = await client.PatchAsJsonAsync($"api/users/{sole.Id}/local-credential", new { isDisabled = true });

        await AssertRefusalAsync(refused, "last_local_credential");
        (await world.CredentialDisabledAsync(sole.Id)).Should().BeFalse();
        (await world.AuditRowsAsync("auth_admin.blocked.last_admin_credential_disabled", sole.Id)).Should().Be(1);

        var repeat = await client.PatchAsJsonAsync($"api/users/{actor.Id}/local-credential", new { isDisabled = true });
        repeat.StatusCode.Should().Be(HttpStatusCode.NoContent, "the credential is already disabled, which is decided before the invariants");
        (await world.AuditRowsAsync("auth_admin.blocked.last_admin_credential_disabled", actor.Id)).Should().Be(0);
    }

    // --------------------------------------------------------------------------------------------- identity links

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_the_local_link_of_the_last_administrator_who_can_sign_in_is_409_last_viable_sign_in_method(bool rcsi)
    {
        var (world, host) = For(rcsi);
        await ClearAdministratorsAsync(world);
        var actor = await StrandedActorAsync(world);
        var sole = await world.NewPersonAsync("sole", Admin);
        await world.AddEntraLinkAsync(sole.Id, Tenant, proven: false);
        using var client = As(host, actor.Username);

        var refused = await client.DeleteAsync($"api/users/{sole.Id}/identity-links?source=Local&externalId={Uri.EscapeDataString(sole.Username)}");

        await AssertRefusalAsync(refused, "last_viable_sign_in_method");
        (await world.LinkCountAsync(sole.Id, 1)).Should().Be(1);
        (await world.AuditRowsAsync("auth_admin.blocked.last_admin_link_removed", "L" + sole.Id)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Removing_the_entra_link_that_is_the_last_way_in_is_409_last_viable_sign_in_method_not_an_unhandled_exception(bool rcsi)
    {
        var (world, host) = For(rcsi, entra: true);
        await ClearAdministratorsAsync(world);
        var actor = await StrandedActorAsync(world);
        var entraOnly = await world.NewPersonAsync("entra-only", Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{entraOnly.Id}';");
        await world.AddEntraLinkAsync(entraOnly.Id, Tenant, proven: true);
        var recovery = await world.NewPersonAsync("recovery", Admin);
        await world.MakeRecoveryAccessHealthyAsync(recovery.Id);
        using var client = As(host, actor.Username);

        var refused = await client.DeleteAsync($"api/users/{entraOnly.Id}/identity-links?source=Entra&externalId=oid-{entraOnly.Id}");

        await AssertRefusalAsync(refused, "last_viable_sign_in_method");
        (await world.LinkCountAsync(entraOnly.Id, 0)).Should().Be(1, "the Entra link is still there");
        (await world.AuditRowsAsync("auth_admin.blocked.last_admin_link_removed", "E" + entraOnly.Id)).Should().Be(1);

        // With another administrator able to sign in, the same removal is allowed.
        await world.NewPersonAsync("spare", Admin);
        var allowed = await client.DeleteAsync($"api/users/{entraOnly.Id}/identity-links?source=Entra&externalId=oid-{entraOnly.Id}");
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.LinkCountAsync(entraOnly.Id, 0)).Should().Be(0);
    }

    // ----------------------------------------------------------------------- untrusted configuration (WP2 review, M-1)

    public static IEnumerable<object[]> UntrustedHostsOnBothModes() =>
        from rcsi in new[] { false, true }
        select new object[] { rcsi };

    [Theory]
    [MemberData(nameof(UntrustedHostsOnBothModes))]
    public async Task M1_an_ordinary_users_change_is_204_and_an_administrators_reduction_is_409_when_the_entra_configuration_cannot_be_trusted(bool rcsi)
    {
        var (world, host) = For(rcsi, entra: false);
        await ClearAdministratorsAsync(world);
        var actor = await StrandedActorAsync(world);

        // The other administrator signs in only through Microsoft, which this host cannot be asked: `viableAdmin` is the only one
        // who signs in locally.
        var spare = await world.NewPersonAsync("spare", Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{spare.Id}';");
        await world.AddEntraLinkAsync(spare.Id, Tenant, proven: true);
        var viableAdmin = await world.NewPersonAsync("viable", Admin);
        var ordinary = await world.NewPersonAsync("ordinary");
        await world.AddCredentialAsync(ordinary.Id);
        await world.AddEntraLinkAsync(ordinary.Id, Tenant, proven: true);
        var withPending = await world.NewPersonAsync("with-pending");
        await world.AddCredentialAsync(withPending.Id);
        await world.AddEntraLinkAsync(withPending.Id, Tenant, proven: false);
        using var client = _hosts.UnreadableHandlerClient(host, actor.Username);

        // The ordinary user's credential, Local link and Entra link: nothing an administrator-viability definition reads moves.
        var disable = await client.PatchAsJsonAsync($"api/users/{ordinary.Id}/local-credential", new { isDisabled = true });
        var removeLocal = await client.DeleteAsync($"api/users/{withPending.Id}/identity-links?source=Local&externalId={Uri.EscapeDataString(withPending.Username)}");
        var removeEntra = await client.DeleteAsync($"api/users/{ordinary.Id}/identity-links?source=Entra&externalId=oid-{ordinary.Id}");

        disable.StatusCode.Should().Be(HttpStatusCode.NoContent);
        removeLocal.StatusCode.Should().Be(HttpStatusCode.NoContent);
        removeEntra.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.CredentialDisabledAsync(ordinary.Id)).Should().BeTrue();
        (await world.LinkCountAsync(withPending.Id, 1)).Should().Be(0);
        (await world.LinkCountAsync(ordinary.Id, 0)).Should().Be(0);
        foreach (var action in new[] { "auth_admin.blocked.last_admin_credential_disabled", "auth_admin.blocked.last_admin_link_removed" })
        {
            (await world.AuditRowsAsync(action, ordinary.Id)).Should().Be(0);
            (await world.AuditRowsAsync(action, "L" + withPending.Id)).Should().Be(0);
            (await world.AuditRowsAsync(action, "E" + ordinary.Id)).Should().Be(0);
        }

        // An administrator's credential, an administrator's role: still fail closed.
        var refusedCredential = await client.PatchAsJsonAsync($"api/users/{viableAdmin.Id}/local-credential", new { isDisabled = true });
        var refusedRole = await client.DeleteAsync($"api/users/{viableAdmin.Id}/roles/{Admin}");

        await AssertRefusalAsync(refusedCredential, "administrator_viability_unavailable");
        await AssertRefusalAsync(refusedRole, "administrator_viability_unavailable");
        (await world.CredentialDisabledAsync(viableAdmin.Id)).Should().BeFalse();
        (await world.AssignmentExistsAsync(viableAdmin.Id, Admin)).Should().BeTrue();

        // Another ordinary administrator who signs in locally makes the same change provably safe without Microsoft: it is allowed.
        await world.NewPersonAsync("local-fallback", Admin);
        var allowedCredential = await client.PatchAsJsonAsync($"api/users/{viableAdmin.Id}/local-credential", new { isDisabled = true });
        allowedCredential.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.CredentialDisabledAsync(viableAdmin.Id)).Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------------------- busy gate

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_busy_gate_is_409_governance_operation_busy_with_retry_for_every_endpoint_and_nothing_changes(bool rcsi)
    {
        var (world, host) = For(rcsi);
        await ClearAdministratorsAsync(world);
        var actor = await StrandedActorAsync(world);
        var target = await world.NewPersonAsync("target", Admin);
        await world.NewPersonAsync("spare", Admin);
        var newcomer = await world.NewPersonAsync("newcomer");
        using var client = _hosts.ImpatientClient(host, actor.Username);

        await using (var holder = await RawSession.BeginAsync(world.Scratch.ConnectionString))
        {
            await holder.ExecuteAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName
                + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;");

            var responses = new[]
            {
                await client.DeleteAsync($"api/users/{target.Id}/roles/{Admin}"),
                await client.PostAsJsonAsync($"api/users/{newcomer.Id}/roles", new { roleId = Admin }),
                await client.PatchAsJsonAsync($"api/users/{target.Id}/local-credential", new { isDisabled = true }),
                await client.DeleteAsync($"api/users/{target.Id}/identity-links?source=Local&externalId={Uri.EscapeDataString(target.Username)}"),
            };

            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.Conflict);
                var body = await BodyAsync(response);
                body.GetProperty("code").GetString().Should().Be("governance_operation_busy");
                body.GetProperty("retry").GetBoolean().Should().BeTrue("contention is transient and safe to retry");
            }

            await holder.RollbackAsync();
        }

        (await world.AssignmentExistsAsync(target.Id, Admin)).Should().BeTrue();
        (await world.AssignmentExistsAsync(newcomer.Id, Admin)).Should().BeFalse();
        (await world.CredentialDisabledAsync(target.Id)).Should().BeFalse();
        (await world.LinkCountAsync(target.Id, 1)).Should().Be(1);

        var retry = await client.DeleteAsync($"api/users/{target.Id}/roles/{Admin}");
        retry.StatusCode.Should().Be(HttpStatusCode.NoContent, "the same request succeeds once the gate is free");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_busy_gate_on_setting_a_local_password_is_409_governance_operation_busy_with_retry_and_changes_nothing(bool rcsi)
    {
        // WP5, SI-1 (A10.2b.15): setting a password takes the administrator-set gate, because it is serialized with a Recovery Access
        // designation (L-P3). A gate held past its bound is the same transient 409 with retry as on the other administrator writers, never
        // an unhandled 500, and nothing is changed or recorded; the same request succeeds once the gate is free.
        const string password = "quiet-maple-lantern-harbor";
        var (world, host) = For(rcsi);
        await ClearAdministratorsAsync(world);
        var actor = await StrandedActorAsync(world);
        var target = await world.NewPersonAsync("target");
        await world.AddCredentialAsync(target.Id);
        Task<string> HashAsync() => world.Scratch.ScalarAsync<string>($"SELECT PasswordHash FROM LocalCredentials WHERE UserId = N'{target.Id}';");
        var before = await HashAsync();
        using var client = _hosts.ImpatientClient(host, actor.Username);

        await using (var holder = await RawSession.BeginAsync(world.Scratch.ConnectionString))
        {
            await holder.ExecuteAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName
                + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0; IF @r < 0 THROW 50000, 'gate not taken', 1;");

            var busy = await client.PostAsJsonAsync($"api/users/{target.Id}/local-credential/password", new { newPassword = password });

            busy.StatusCode.Should().Be(HttpStatusCode.Conflict, "a gate held past its bound is a transient conflict, not an unhandled error");
            var body = await BodyAsync(busy);
            body.GetProperty("code").GetString().Should().Be("governance_operation_busy");
            body.GetProperty("retry").GetBoolean().Should().BeTrue("contention is transient and safe to retry");
            body.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
            await holder.RollbackAsync();
        }

        (await HashAsync()).Should().Be(before, "nothing was changed");
        (await world.AuditRowsAsync("credential.password_set", target.Id)).Should().Be(0, "a busy gate writes no row");

        var retry = await client.PostAsJsonAsync($"api/users/{target.Id}/local-credential/password", new { newPassword = password });
        retry.StatusCode.Should().Be(HttpStatusCode.NoContent, "the same request succeeds once the gate is free");
        (await HashAsync()).Should().NotBe(before);
        (await world.AuditRowsAsync("credential.password_set", target.Id)).Should().Be(1);
    }
}
