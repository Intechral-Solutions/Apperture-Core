using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
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
    public async Task Options_and_the_oidc_callback_and_get_signout_are_not_rule_u_rejections()
    {
        RequireSql();
        using var client = _fixture.Factory!.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Remove(BrowserRequest.HeaderName);

        var options = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/auth/local/login"));
        (await options.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);

        var callback = await client.PostAsync("/signin-oidc", new StringContent(""));
        (await callback.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);

        var signout = await client.GetAsync("/signout");
        signout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await signout.Content.ReadAsStringAsync()).Should().NotContain(BrowserRequestTrustRejection);
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
        db.Users.Add(new UserEntity { Id = userId, DisplayName = externalId, IsBreakGlassEligible = false });
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
