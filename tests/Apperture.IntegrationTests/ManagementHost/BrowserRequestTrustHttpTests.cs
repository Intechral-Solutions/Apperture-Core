using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// SEC-1B1 on the real Management.Host pipeline: exact origin, platform header, fetch metadata,
/// the enroll exemption, and no mutation or audit when the policy rejects.
/// </summary>
[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class BrowserRequestTrustHttpTests
{
    private readonly ManagementHostGovernanceFixture _fixture;

    public BrowserRequestTrustHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_trusted_login_reaches_authentication_and_an_untrusted_login_is_403_with_no_audit()
    {
        RequireSql();
        using var trusted = _fixture.CreateClient();
        var reached = await trusted.PostAsJsonAsync("/api/auth/local/login", new { username = "nobody", password = "not-a-password" });
        reached.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var before = await CountAuditAsync("auth.local.login.failed");
        using var untrusted = Untrusted();
        var blocked = await untrusted.PostAsJsonAsync("/api/auth/local/login", new { username = "nobody", password = "not-a-password" });
        await AssertBlocked(blocked);
        (await CountAuditAsync("auth.local.login.failed")).Should().Be(before);
    }

    [Theory]
    [InlineData("PUT", "/api/account/preferences")]
    [InlineData("PATCH", "/api/connections/does-not-exist")]
    [InlineData("DELETE", "/api/connections/does-not-exist")]
    public async Task Trusted_unsafe_methods_reach_the_pipeline_and_untrusted_ones_do_not(string method, string path)
    {
        RequireSql();
        using var trusted = _fixture.CreateClient();
        using var trustedMessage = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "DELETE")
            trustedMessage.Content = JsonContent.Create(new { });
        (await trusted.SendAsync(trustedMessage)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var untrusted = Untrusted();
        using var blockedMessage = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "DELETE")
            blockedMessage.Content = JsonContent.Create(new { });
        await AssertBlocked(await untrusted.SendAsync(blockedMessage));
    }

    [Fact]
    public async Task A_same_site_sibling_origin_and_a_cross_site_fetch_are_rejected()
    {
        RequireSql();
        using var sibling = Untrusted();
        sibling.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "http://localhost:9002");
        sibling.DefaultRequestHeaders.TryAddWithoutValidation(BrowserRequest.HeaderName, BrowserRequest.HeaderValue);
        await AssertBlocked(await sibling.PostAsJsonAsync("/api/auth/local/login", new { username = "nobody", password = "x" }));

        using var crossSite = _fixture.CreateClient();
        crossSite.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        await AssertBlocked(await crossSite.PostAsJsonAsync("/api/auth/local/login", new { username = "nobody", password = "x" }));
    }

    [Fact]
    public async Task Get_reads_stay_open_unless_the_browser_says_cross_site()
    {
        RequireSql();
        using var plain = Untrusted();
        (await plain.GetAsync("/api/auth/sign-in-options")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var crossSite = Untrusted();
        crossSite.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        await AssertBlocked(await crossSite.GetAsync("/api/auth/sign-in-options"));
    }

    [Fact]
    public async Task Options_and_the_oidc_callbacks_are_not_rule_u_rejections()
    {
        RequireSql();
        using var client = Navigation(origin: null);

        var options = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/auth/local/login"));
        (await options.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);
        options.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse("Testing does not apply UiDev CORS");
        options.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        options.Headers.GetValues("Content-Security-Policy").Should().Contain("frame-ancestors 'none'");

        var callback = await client.PostAsync("/signin-oidc", new StringContent(""));
        (await callback.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);

        var signedOut = await client.GetAsync("/signout-callback-oidc");
        signedOut.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "the IdP's post-logout redirect is a protocol path, not Rule N");

        var remoteSignOut = await client.GetAsync("/signout-oidc");
        remoteSignOut.StatusCode.Should().Be(HttpStatusCode.NotFound, "RemoteSignOutPath is empty; the historical path is not a protocol endpoint");
        remoteSignOut.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
    }

    // ---- SEC-1B2: Rule N on the real pipeline (sign-out form, step-up and link navigations) -----------------------------------

    [Fact]
    public async Task Sign_out_from_the_trusted_origin_needs_no_platform_header_and_returns_to_login()
    {
        RequireSql();
        using var client = Navigation(ManagementHostWebApplicationFactory.TrustedUiOrigin);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");

        var response = await client.PostAsync("/signout", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("http://localhost:9001/login");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("null", null)]
    [InlineData("http://localhost:9002", "same-site")]
    [InlineData("http://127.0.0.1:9001", "cross-site")]
    [InlineData(ManagementHostWebApplicationFactory.TrustedUiOrigin, "cross-site")]
    public async Task Sign_out_without_the_exact_trusted_origin_or_marked_cross_site_is_403_and_clears_nothing(string? origin, string? fetchSite)
    {
        RequireSql();
        using var client = Navigation(origin);
        client.DefaultRequestHeaders.TryAddWithoutValidation(BrowserRequest.HeaderName, BrowserRequest.HeaderValue);
        if (fetchSite is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);

        var response = await client.PostAsync("/signout", content: null);

        await AssertBlocked(response);
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse("a rejected sign-out never reaches the cookie handler");
    }

    [Fact]
    public async Task Get_sign_out_is_405_and_never_a_redirect()
    {
        RequireSql();
        using var client = Navigation(origin: null);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        var response = await client.GetAsync("/signout");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Headers.Location.Should().BeNull();
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("/signin/step-up")]
    [InlineData("/signin/link")]
    public async Task A_cross_site_step_up_or_link_navigation_is_403_and_other_navigations_reach_the_endpoint(string path)
    {
        RequireSql();
        using var crossSite = Navigation(origin: null);
        crossSite.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        await AssertBlocked(await crossSite.GetAsync(path));

        foreach (var fetchSite in new[] { "same-site", "same-origin", "none", null })
        {
            using var client = Navigation(origin: null);
            if (fetchSite is not null)
                client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", fetchSite);
            var response = await client.GetAsync(path);
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"Sec-Fetch-Site={fetchSite ?? "(absent)"} is not a cross-site navigation");
            (await response.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);
        }
    }

    [Fact]
    public async Task The_anonymous_sign_in_entry_point_stays_open_to_cross_site_navigation()
    {
        RequireSql();
        using var client = Navigation(origin: null);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        var response = await client.GetAsync("/signin");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);
    }

    // ---- SEC-1B2: the Entra directory searches (G1/G2) are unsafe requests under Rule U -------------------------------------

    [Fact]
    public async Task A_directory_search_that_rule_u_rejects_makes_no_graph_call_and_writes_no_discovery_outcome()
    {
        RequireSql();
        var reader = "b2-auth-reader-" + Guid.NewGuid().ToString("N")[..8];
        await EnsureAuthConfigurationReaderAsync(reader);
        var registrationId = await AddRegistrationAsync();
        var discovery = new CountingDirectoryDiscovery();
        using var factory = _fixture.Factory!.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IEntraDirectoryDiscoveryService>(discovery)));

        foreach (var kind in new[] { "users", "groups" })
        {
            var path = $"/api/config/auth/entra-tenants/{registrationId}/directory/{kind}";
            var body = new { search = "dana", top = 5 };

            using var untrusted = factory.CreateClient();
            untrusted.DefaultRequestHeaders.Remove("Origin");
            untrusted.DefaultRequestHeaders.Remove(BrowserRequest.HeaderName);
            untrusted.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, reader);
            await AssertBlocked(await untrusted.PostAsJsonAsync(path, body));

            using var sibling = factory.CreateClient();
            sibling.DefaultRequestHeaders.Remove("Origin");
            sibling.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "http://localhost:9002");
            sibling.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
            sibling.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, reader);
            await AssertBlocked(await sibling.PostAsJsonAsync(path, body));

            using var get = factory.CreateClient();
            get.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, reader);
            get.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
            (await get.GetAsync(path + "?search=dana&top=5")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "the old GET route is gone");
        }

        discovery.Calls.Should().Be(0, "no rejected or GET request reached Microsoft Graph");
        (await DiscoveryOutcomeAsync(registrationId)).Should().BeNull("no rejected or GET request wrote the discovery outcome");

        using var trusted = factory.CreateClient();
        trusted.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, reader);
        var users = await trusted.PostAsJsonAsync($"/api/config/auth/entra-tenants/{registrationId}/directory/users", new { search = "dana", top = 5 });
        users.StatusCode.Should().Be(HttpStatusCode.OK);
        (await users.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0].GetProperty("objectId").GetString().Should().Be("oid-dana");
        discovery.Calls.Should().Be(1);
        discovery.LastSearch.Should().Be("dana");
        discovery.LastTop.Should().Be(5);
        (await DiscoveryOutcomeAsync(registrationId)).Should().NotBeNull("the trusted POST is the path that records the outcome");
    }

    private HttpClient Navigation(string? origin)
    {
        var client = _fixture.Factory!.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Remove(BrowserRequest.HeaderName);
        if (origin is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
        return client;
    }

    private async Task EnsureAuthConfigurationReaderAsync(string externalId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var roleId = "role-" + externalId;
        var userId = "user-" + externalId;
        db.Roles.Add(new RoleEntity
        {
            Id = roleId,
            Name = "Directory search test " + externalId,
            Description = "test",
            PermissionsJson = """[{"action":"auth.configuration.read","resource":{"resourceType":"InstanceConfiguration","scopeKey":"instance"}}]""",
            IsSystemRole = false,
        });
        db.Users.Add(new UserEntity { Id = userId, Username = externalId, DisplayName = externalId, IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity { Id = "link-" + externalId, UserId = userId, Source = (int)IdentitySource.Local, ExternalId = externalId });
        db.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    private async Task<string> AddRegistrationAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var id = "b2reg" + Guid.NewGuid().ToString("N")[..12];
        var now = DateTimeOffset.UtcNow;
        db.EntraInstanceRegistrations.Add(new EntraInstanceRegistrationEntity
        {
            Id = id,
            DisplayName = "Directory search test " + id,
            Authority = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ClientId = "22222222-2222-2222-2222-222222222222",
            AzureTenantId = "11111111-1111-1111-1111-111111111111",
            UsesDeploymentSecret = true,
            Enabled = true,
            IsActive = false,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<DateTimeOffset?> DiscoveryOutcomeAsync(string registrationId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.EntraInstanceRegistrations.AsNoTracking()
            .Where(r => r.Id == registrationId)
            .Select(r => r.LastDirectoryDiscoveryUtc)
            .SingleAsync();
    }

    private sealed class CountingDirectoryDiscovery : IEntraDirectoryDiscoveryService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string? LastSearch { get; private set; }

        public int LastTop { get; private set; }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default)
        {
            Record(search, top);
            return Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>(
                true, DiscoveryFailureCategory.None, "ok", null, [new EntraDirectoryUser("oid-dana", "Dana", "dana@contoso.com", null)]));
        }

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryUser>>> SearchUsersAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, string? skipToken, CancellationToken cancellationToken = default) =>
            SearchUsersAsync(context, search, top, cancellationToken);

        public Task<DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>> SearchGroupsAsync(
            EntraDirectoryDiscoveryContext context, string? search, int top, CancellationToken cancellationToken = default)
        {
            Record(search, top);
            return Task.FromResult(new DirectoryDiscoveryResult<IReadOnlyList<EntraDirectoryGroup>>(true, DiscoveryFailureCategory.None, "ok", null, []));
        }

        public Task<DirectoryDiscoveryResult<EntraDirectoryUser?>> GetUserByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used by the directory search");

        public Task<DirectoryDiscoveryResult<EntraDirectoryGroup?>> GetGroupByIdAsync(
            EntraDirectoryDiscoveryContext context, string objectId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used by the directory search");

        private void Record(string? search, int top)
        {
            Interlocked.Increment(ref _calls);
            LastSearch = search;
            LastTop = top;
        }
    }

    [Fact]
    public async Task Post_enroll_is_exempt_and_the_invitation_neighbor_is_not()
    {
        RequireSql();
        using var client = Untrusted();
        var enroll = await client.PostAsJsonAsync("/api/enroll", new { });
        (await enroll.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);

        var invitation = await client.PostAsJsonAsync("/api/enroll/invitation", new { runtimeId = "runtime-1" });
        await AssertBlocked(invitation);

        var lookalike = await client.PostAsJsonAsync("/api/enroll/extra", new { });
        await AssertBlocked(lookalike);
    }

    [Fact]
    public async Task An_untrusted_connection_create_does_not_write_a_row_or_an_audit_record()
    {
        RequireSql();
        var externalId = "b1-author-" + Guid.NewGuid().ToString("N")[..8];
        await EnsureAuthorAsync(externalId);
        var name = "Blocked connection " + Guid.NewGuid().ToString("N")[..8];
        var beforeRows = await CountConnectionsAsync();
        var beforeAudit = await CountAuditAsync("connection.created");

        using var blocked = Untrusted();
        blocked.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, externalId);
        await AssertBlocked(await blocked.PostAsJsonAsync("/api/connections", ConnectionBody(name)));

        (await CountConnectionsAsync()).Should().Be(beforeRows);
        (await CountAuditAsync("connection.created")).Should().Be(beforeAudit);

        using var author = _fixture.CreateAuthenticatedClient(externalId);
        var created = await author.PostAsJsonAsync("/api/connections", ConnectionBody(name));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        using var blockedPatch = Untrusted();
        blockedPatch.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, externalId);
        await AssertBlocked(await blockedPatch.PatchAsJsonAsync($"/api/connections/{id}", new { name = name + " forged" }));

        using var authorPatch = _fixture.CreateAuthenticatedClient(externalId);
        (await authorPatch.PatchAsJsonAsync($"/api/connections/{id}", new { name = name + " renamed" })).StatusCode.Should().Be(HttpStatusCode.OK);

        using var blockedDelete = Untrusted();
        blockedDelete.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, externalId);
        await AssertBlocked(await blockedDelete.DeleteAsync($"/api/connections/{id}"));
        (await author.GetAsync($"/api/connections/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await author.DeleteAsync($"/api/connections/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_empty_launch_body_reaches_the_action_only_when_the_request_is_trusted()
    {
        RequireSql();
        const string path = "/api/integrations/intgovernmissing00000001/procedures/Sample.HelloWorld.procedures.opPing/launch";
        using var blocked = Untrusted();
        blocked.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        await AssertBlocked(await blocked.PostAsync(path, content: null));

        using var trusted = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalProcedureLauncher);
        var reached = await trusted.PostAsync(path, content: null);
        reached.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var doc = JsonDocument.Parse(await reached.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("integration_not_found");
    }

    private const string BrowserRequestTrustRejection = "request_origin_rejected";

    private void RequireSql() =>
        _fixture.SqlAvailable.Should().BeTrue("the fixture fails hard: " + SqlServerTestConfiguration.DescribeActiveConfiguration());

    private HttpClient Untrusted()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Remove(BrowserRequest.HeaderName);
        return client;
    }

    private static async Task AssertBlocked(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"title\":\"Request blocked\"").And.Contain("\"code\":\"request_origin_rejected\"");
        response.Headers.WwwAuthenticate.Should().BeEmpty();
    }

    private async Task<int> CountAuditAsync(string action)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.AuditLogs.CountAsync(row => row.Action == action);
    }

    private async Task<int> CountConnectionsAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.Connections.CountAsync();
    }

    private async Task EnsureAuthorAsync(string externalId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var roleId = "role-" + externalId;
        var userId = "user-" + externalId;
        db.Roles.Add(new RoleEntity
        {
            Id = roleId,
            Name = "Browser trust test " + externalId,
            Description = "test",
            PermissionsJson = """[{"action":"connection.create","resource":{"resourceType":"Connection","scopeKey":"instance"}},{"action":"connection.read","resource":{"resourceType":"Connection","scopeKey":"instance"}},{"action":"connection.update","resource":{"resourceType":"Connection","scopeKey":"instance"}},{"action":"connection.delete","resource":{"resourceType":"Connection","scopeKey":"instance"}}]""",
            IsSystemRole = false,
        });
        db.Users.Add(new UserEntity { Id = userId, Username = externalId, DisplayName = externalId, IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity { Id = "link-" + externalId, UserId = userId, Source = (int)IdentitySource.Local, ExternalId = externalId });
        db.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    private static object ConnectionBody(string name) => new
    {
        connectorDefinitionId = WellKnownConnectorDefinitionIds.PlatformSql.Value,
        name,
        config = new Dictionary<string, string>
        {
            ["Server"] = "127.0.0.1",
            ["Database"] = "master",
            ["UserId"] = "sa",
            ["TrustServerCertificate"] = "true",
        },
        secretValues = new Dictionary<string, string> { ["Password"] = "not-used" },
    };
}
