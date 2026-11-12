using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.AdministratorViability;
using Apperture.IntegrationTests.Audit;
using FluentAssertions;
using Apperture.Management.Host;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.ManagementHost;

using HostFactory = WebApplicationFactory<ManagementHostWebApplicationFactoryEntryPoint>;

/// <summary>
/// IAM-10.2b WP3 at the real Management Host boundary (A10.2b.7, A10.2b.15): the statuses, stable codes and bodies of the Entra
/// registration and sign-in policy endpoints. A security refusal is a typed, actionable 409; a malformed request is a 400, a missing
/// permission a 403 and an unknown registration a 404; a busy gate is a 409 with <c>retry</c>; an infrastructure failure is never turned
/// into a business refusal. Uses the hosts of <see cref="AdministratorViabilityHostHttpTests"/> that start with a usable registration
/// (handler bound to its tenant), one per isolation mode, each on a throwaway catalog. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class EntraRegistrationViabilityHostHttpTests : IClassFixture<AdministratorViabilityHostHttpTests.Hosts>
{
    private const string Admin = "role-platform-administrator";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "22222222-2222-2222-2222-222222222222";
    private const string Route = "api/config/auth/entra-tenants";

    private readonly AdministratorViabilityHostHttpTests.Hosts _hosts;

    public EntraRegistrationViabilityHostHttpTests(AdministratorViabilityHostHttpTests.Hosts hosts) => _hosts = hosts;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private (RoleAuthorityWorld World, HostFactory Host) For(bool rcsi) =>
        rcsi
            ? (new RoleAuthorityWorld(_hosts.EntraRcsi), _hosts.EntraRcsiHost)
            : (new RoleAuthorityWorld(_hosts.EntraLocking), _hosts.EntraLockingHost);

    private static HttpClient As(HostFactory host, string username)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, username);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>One registration for <see cref="Tenant"/>, active, usable with the host's deployment secret; nobody else can sign in but the reliant administrator.</summary>
    private sealed record Arranged(RoleAuthorityWorld World, HostFactory Host, string RegistrationId, string Reliant, RoleAuthorityWorld.Person Actor);

    private async Task<Arranged> ArrangeAsync(bool rcsi)
    {
        var (world, host) = For(rcsi);
        await world.Scratch.ExecuteAsync($"""
            DELETE FROM UserMfaRecoveryCodes;
            DELETE FROM UserTotpCredentials;
            DELETE FROM UserRoles WHERE RoleId = N'{Admin}';
            UPDATE Users SET IsBreakGlassEligible = 0 WHERE IsBreakGlassEligible = 1;
            """);
        await world.SetActiveRegistrationAsync(Tenant);
        var registrationId = await world.Scratch.ScalarAsync<string>("SELECT Id FROM EntraInstanceRegistrations;");
        var actor = await world.NewPersonAsync("actor", Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{actor.Id}';");
        var reliant = await world.NewUserAsync("reliant", Admin);
        await world.Scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{reliant}';");
        await world.AddEntraLinkAsync(reliant, Tenant, proven: true);
        var recovery = await world.NewPersonAsync("recovery", Admin);
        await world.MakeRecoveryAccessHealthyAsync(recovery.Id);
        return new Arranged(world, host, registrationId!, reliant, actor);
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

    private static async Task<JsonElement> GetRegistrationAsync(HttpClient client, string id) =>
        await BodyAsync(await client.GetAsync($"{Route}/{id}"));

    private static object UpdateBody(JsonElement current, Action<Dictionary<string, object?>>? edit = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["displayName"] = current.GetProperty("displayName").GetString(),
            ["authority"] = current.GetProperty("authority").GetString(),
            ["clientId"] = current.GetProperty("clientId").GetString(),
            ["usesDeploymentSecret"] = true,
            ["enabled"] = current.GetProperty("enabled").GetBoolean(),
            ["azureTenantId"] = current.GetProperty("azureTenantId").GetString(),
            ["allowMemberAccessRequests"] = false,
            ["allowGuestAccessRequests"] = false,
            ["rowVersionBase64"] = current.GetProperty("rowVersionBase64").GetString(),
        };
        edit?.Invoke(body);
        return body;
    }

    // ---------------------------------------------------------------------------------------------------- 409

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Deleting_the_registration_the_last_viable_administrator_signs_in_through_is_a_typed_409_and_allowed_once_another_can_sign_in(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        using var client = As(a.Host, a.Actor.Username);
        var refusalsBefore = await a.World.AuditRowsAsync("entra.tenant_registration.change_refused", a.RegistrationId);

        var refused = await client.DeleteAsync($"{Route}/{a.RegistrationId}");

        await AssertRefusalAsync(refused, "last_viable_sign_in_method");
        (await a.World.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{a.RegistrationId}';")).Should().Be(1);
        (await a.World.AuditRowsAsync("entra.tenant_registration.change_refused", a.RegistrationId)).Should().Be(refusalsBefore + 1, "the audit table is append-only and the registration id is the same in every case");

        await a.World.NewPersonAsync("local", Admin);
        var allowed = await client.DeleteAsync($"{Route}/{a.RegistrationId}");
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Disabling_changing_the_tenant_of_and_switching_away_from_the_registration_are_all_typed_409s_and_change_nothing(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        using var client = As(a.Host, a.Actor.Username);
        var current = await GetRegistrationAsync(client, a.RegistrationId);
        await a.World.Scratch.ExecuteAsync($"""
            INSERT INTO EntraInstanceRegistrations
                (Id, DisplayName, Authority, ClientId, UsesDeploymentSecret, Enabled, IsActive, CreatedUtc, UpdatedUtc, AzureTenantId, AllowMemberAccessRequests, AllowGuestAccessRequests)
            VALUES (N'second-registration', N'Second', N'https://login.microsoftonline.com/{OtherTenant}/v2.0', N'client', 1, 1, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{OtherTenant}', 0, 0);
            """);

        var disable = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["enabled"] = false));
        var moveTenant = await client.PatchAsJsonAsync(
            $"{Route}/{a.RegistrationId}",
            UpdateBody(current, b => { b["authority"] = $"https://login.microsoftonline.com/{OtherTenant}/v2.0"; b["azureTenantId"] = OtherTenant; }));
        var @switch = await client.PostAsync($"{Route}/second-registration/activate", content: null);

        await AssertRefusalAsync(disable, "last_viable_sign_in_method");
        await AssertRefusalAsync(moveTenant, "last_viable_sign_in_method");
        await AssertRefusalAsync(@switch, "last_viable_sign_in_method");
        var after = await GetRegistrationAsync(client, a.RegistrationId);
        after.GetProperty("enabled").GetBoolean().Should().BeTrue();
        after.GetProperty("isActive").GetBoolean().Should().BeTrue();
        after.GetProperty("azureTenantId").GetString().Should().Be(Tenant);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_stale_configuration_is_409_administrator_viability_unavailable_for_a_reducing_change_and_the_registration_is_untouched(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        using var client = _hosts.UnreadableHandlerClient(a.Host, a.Actor.Username);
        var current = await GetRegistrationAsync(client, a.RegistrationId);

        var refused = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["enabled"] = false));

        await AssertRefusalAsync(refused, "administrator_viability_unavailable");
        (await GetRegistrationAsync(client, a.RegistrationId)).GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    // ------------------------------------------------------------------------------------- 400, 403, 404, no-op, success

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Malformed_requests_are_400_unknown_registrations_404_and_a_missing_permission_403_none_of_them_a_refusal_row(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        using var client = As(a.Host, a.Actor.Username);
        var current = await GetRegistrationAsync(client, a.RegistrationId);
        var nobody = await a.World.NewPersonAsync("nobody");
        using var unprivileged = As(a.Host, nobody.Username);
        var refusalsBefore = await a.World.AuditRowsAsync("entra.tenant_registration.change_refused", a.RegistrationId);

        var noRowVersion = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["rowVersionBase64"] = null));
        var badBase64 = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["rowVersionBase64"] = "not base 64!"));
        var badContext = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["authenticationContextId"] = "nope"));
        var notDeploymentSecret = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["usesDeploymentSecret"] = false));
        var missing = await client.PatchAsJsonAsync($"{Route}/does-not-exist", UpdateBody(current));
        var missingActivate = await client.PostAsync($"{Route}/does-not-exist/activate", content: null);
        var forbidden = await unprivileged.DeleteAsync($"{Route}/{a.RegistrationId}");

        noRowVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badBase64.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badContext.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        notDeploymentSecret.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingActivate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await a.World.AuditRowsAsync("entra.tenant_registration.change_refused", a.RegistrationId)).Should().Be(refusalsBefore);
        (await a.World.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{a.RegistrationId}';")).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_unchanged_edit_is_a_200_no_op_and_a_same_tenant_edit_is_a_200_for_the_last_viable_administrator(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        using var client = As(a.Host, a.Actor.Username);
        var current = await GetRegistrationAsync(client, a.RegistrationId);

        var noOp = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current));
        var rename = await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["displayName"] = "Renamed"));

        noOp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.World.AuditRowsAsync("entra.tenant_registration.updated", a.RegistrationId)).Should().Be(1, "only the rename was recorded");
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(rename)).GetProperty("displayName").GetString().Should().Be("Renamed");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Creating_a_registration_is_201_even_when_nobody_can_sign_in(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        await a.World.Scratch.ExecuteAsync("UPDATE LocalCredentials SET IsDisabled = 1;");
        using var client = As(a.Host, a.Actor.Username);

        var created = await client.PostAsJsonAsync(Route, new
        {
            displayName = "Created over HTTP",
            authority = $"https://login.microsoftonline.com/{OtherTenant}/v2.0",
            clientId = "22222222-2222-2222-2222-222222222222",
            usesDeploymentSecret = true,
            enabled = true,
            azureTenantId = OtherTenant,
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await BodyAsync(created)).GetProperty("isActive").GetBoolean().Should().BeFalse("a new registration is never active");
    }

    // ------------------------------------------------------------------------------------------------- busy gate

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_busy_gate_is_409_governance_operation_busy_with_retry_for_every_registration_and_policy_endpoint_and_nothing_changes(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        await a.World.NewPersonAsync("local", Admin);
        using var client = _hosts.ImpatientClient(a.Host, a.Actor.Username);
        var current = await GetRegistrationAsync(client, a.RegistrationId);
        var policy = await BodyAsync(await client.GetAsync("api/config/auth-ui-policy"));
        var before = await a.World.Scratch.ScalarAsync<string>("SELECT CONCAT(Id, ':', Enabled, ':', IsActive, ':', DisplayName) FROM EntraInstanceRegistrations;");

        await using (var holder = await RawSession.BeginAsync(a.World.Scratch.ConnectionString))
        {
            await holder.ExecuteAsync(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'" + PlatformAdministratorSetLock.ResourceName
                + "', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;");

            var responses = new[]
            {
                await client.PostAsJsonAsync(Route, new { displayName = "Busy", authority = $"https://login.microsoftonline.com/{OtherTenant}/v2.0", clientId = "client", usesDeploymentSecret = true, enabled = true }),
                await client.PatchAsJsonAsync($"{Route}/{a.RegistrationId}", UpdateBody(current, b => b["displayName"] = "Busy")),
                await client.DeleteAsync($"{Route}/{a.RegistrationId}"),
                await client.PostAsync($"{Route}/{a.RegistrationId}/activate", content: null),
                await client.PatchAsJsonAsync("api/config/auth-ui-policy", new
                {
                    showLocalSignInInPrimaryChooser = true,
                    preferEntraInChooser = true,
                    autoRedirectToEntra = false,
                    rowVersionBase64 = policy.GetProperty("rowVersionBase64").GetString(),
                }),
            };

            // Activating the registration that is already active is decided inside the unit, under the gate, so it waits for it as well.
            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.Conflict);
                var body = await BodyAsync(response);
                body.GetProperty("code").GetString().Should().Be("governance_operation_busy");
                body.GetProperty("retry").GetBoolean().Should().BeTrue("contention is transient and safe to retry");
            }

            await holder.RollbackAsync();
        }

        (await a.World.Scratch.ScalarAsync<string>("SELECT CONCAT(Id, ':', Enabled, ':', IsActive, ':', DisplayName) FROM EntraInstanceRegistrations;")).Should().Be(before);
        (await client.DeleteAsync($"{Route}/{a.RegistrationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent, "the same request succeeds once the gate is free");
    }

    // ------------------------------------------------------------------------------------------------ a fresh host

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_fresh_host_whose_very_first_request_is_a_registration_change_decides_it_without_hanging_or_freezing_the_configuration(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_VbFresh", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        await world.SetActiveRegistrationAsync(Tenant);
        var registrationId = (await scratch.ScalarAsync<string>("SELECT Id FROM EntraInstanceRegistrations;"))!;
        var actor = await world.NewPersonAsync("actor", Admin);
        await scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{actor.Id}';");
        var reliant = await world.NewUserAsync("reliant", Admin);
        await scratch.ExecuteAsync($"UPDATE LocalCredentials SET IsDisabled = 1 WHERE UserId = N'{reliant}';");
        await world.AddEntraLinkAsync(reliant, Tenant, proven: true);

        // Nothing has asked this host for anything yet: the registration change is the first request it ever serves.
        await using var host = new ManagementHostWebApplicationFactory(scratch.ConnectionString)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Entra:ClientSecret"] = "host-test-secret" })));
        using var client = As(host, actor.Username);

        var first = await client.DeleteAsync($"{Route}/{registrationId}").WaitAsync(TimeSpan.FromSeconds(60));

        await AssertRefusalAsync(first, "last_viable_sign_in_method");
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{registrationId}';")).Should().Be(1);

        await world.NewPersonAsync("local", Admin);
        var second = await client.DeleteAsync($"{Route}/{registrationId}").WaitAsync(TimeSpan.FromSeconds(60));
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{registrationId}';")).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------- a persistent secret-store failure

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_stored_secret_the_host_cannot_unprotect_refuses_a_reducing_change_until_a_local_administrator_can_sign_in_and_then_allows_it(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        using var client = As(a.Host, a.Actor.Username);

        // The registration names a stored secret whose payload the host's protector (DPAPI) rejects: the failure does not go away on
        // its own, and a restart would meet the same payload. What the host can accept from Microsoft is therefore not established.
        var secretId = Guid.NewGuid();
        await a.World.Scratch.ExecuteAsync($"""
            INSERT INTO SecretDescriptors (Id, Name, Provider, MaterialType, ActiveVersion, State, CreatedAt)
            VALUES ('{secretId}', N'unreadable-{secretId:N}', N'EntraRegistration', 0, N'1', 1, SYSDATETIMEOFFSET());
            INSERT INTO SecretVersions (SecretId, Version, State, ProtectedPayload, KeyId, CreatedAt)
            VALUES ('{secretId}', N'1', 1, 0x0102030405060708, N'dpapi-localmachine', SYSDATETIMEOFFSET());
            UPDATE EntraInstanceRegistrations SET ClientSecretRef = N'{secretId}' WHERE Id = N'{a.RegistrationId}';
            """);

        // Nobody signs in locally: the change cannot be shown safe, so it is refused as unavailable, with nothing changed.
        var refused = await client.DeleteAsync($"{Route}/{a.RegistrationId}");
        await AssertRefusalAsync(refused, "administrator_viability_unavailable");
        (await a.World.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{a.RegistrationId}';")).Should().Be(1);
        (await a.World.Scratch.ScalarAsync<int>($"SELECT State FROM SecretDescriptors WHERE Id = '{secretId}';")).Should().Be(1, "a refused delete retires nothing");

        // An ordinary administrator who signs in locally makes the same change provably safe without Microsoft.
        await a.World.NewPersonAsync("local", Admin);
        var allowed = await client.DeleteAsync($"{Route}/{a.RegistrationId}");
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await a.World.Scratch.ScalarAsync<int>($"SELECT COUNT(*) FROM EntraInstanceRegistrations WHERE Id = N'{a.RegistrationId}';")).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------- sign-in policy

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_chooser_only_policy_change_is_200_and_never_a_viability_refusal_a_stale_one_is_409_and_an_unchanged_one_a_200_no_op(bool rcsi)
    {
        var a = await ArrangeAsync(rcsi);
        using var client = As(a.Host, a.Actor.Username);
        var policy = await BodyAsync(await client.GetAsync("api/config/auth-ui-policy"));
        object Body(bool prefer, string? rowVersion) => new
        {
            showLocalSignInInPrimaryChooser = true,
            preferEntraInChooser = prefer,
            autoRedirectToEntra = false,
            rowVersionBase64 = rowVersion,
        };
        var version = policy.GetProperty("rowVersionBase64").GetString();

        var changed = await client.PatchAsJsonAsync("api/config/auth-ui-policy", Body(true, version));
        var stale = await client.PatchAsJsonAsync("api/config/auth-ui-policy", Body(false, version));
        var current = await BodyAsync(await client.GetAsync("api/config/auth-ui-policy"));
        var unchanged = await client.PatchAsJsonAsync("api/config/auth-ui-policy", Body(true, current.GetProperty("rowVersionBase64").GetString()));

        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(stale)).TryGetProperty("code", out _).Should().BeFalse("the row-version conflict keeps its existing body");
        unchanged.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(unchanged)).GetProperty("preferEntraInChooser").GetBoolean().Should().BeTrue();
    }
}
