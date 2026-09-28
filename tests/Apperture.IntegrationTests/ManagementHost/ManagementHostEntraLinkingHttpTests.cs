using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostEntraLinkingHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string ActiveTenantId = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenantId = "22222222-2222-2222-2222-222222222222";

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostEntraLinkingHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    [Fact]
    public async Task Admin_client_creates_a_user_when_an_earlier_local_username_is_not_an_administrator()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        _ = await CreateLocalUserAsync(admin, $"aaa-{Guid.NewGuid():N}"[..16]);

        var adminAgain = await EnsureAdminClientAsync();
        var createdId = await CreateLocalUserAsync(adminAgain, $"bbb-{Guid.NewGuid():N}"[..16]);
        createdId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Admin_can_add_entra_link_to_existing_user()
    {
        AssertSqlAvailable();
        await EnsureActiveRegistrationAsync();
        var admin = await EnsureAdminClientAsync();
        var targetUserId = await CreateLocalUserAsync(admin, $"link-target-{Guid.NewGuid():N}"[..16]);

        var entraOid = Guid.NewGuid().ToString();
        var response = await admin.PostAsJsonAsync(
            $"/api/users/{targetUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Linked_entra_principal_resolves_via_auth_me()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        var username = $"entra-target-{Guid.NewGuid():N}"[..16];
        var entraOid = Guid.NewGuid().ToString();
        var targetUserId = await CreateLocalUserAsync(admin, username);
        _ = await admin.PostAsJsonAsync(
            $"/api/users/{targetUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));

        var entraClient = CreateTestClient(entraOid, "Entra");
        var me = await entraClient.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        body!.UserId.Should().Be(targetUserId);
        body.SignInSource.Should().Be("Entra");
        body.IdentityLinks.Should().Contain(l => l.Source == "Entra");
    }

    [Fact]
    public async Task Unlinked_entra_principal_returns_forbidden_on_auth_me()
    {
        AssertSqlAvailable();
        await EnsureAdminClientAsync();

        var client = CreateTestClient("unlinked-entra-oid-99999999", "Entra");
        var me = await client.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Duplicate_entra_link_on_same_user_is_idempotent()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        var entraOid = Guid.NewGuid().ToString();
        var targetUserId = await CreateLocalUserAsync(admin, $"dup-target-{Guid.NewGuid():N}"[..16]);
        var request = new AddIdentityLinkRequest("Entra", entraOid);
        _ = await admin.PostAsJsonAsync($"/api/users/{targetUserId}/identity-links", request);

        var second = await admin.PostAsJsonAsync($"/api/users/{targetUserId}/identity-links", request);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Entra_external_id_already_linked_to_another_user_returns_conflict()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        var entraOid = Guid.NewGuid().ToString();
        var firstUserId = await CreateLocalUserAsync(admin, $"first-{Guid.NewGuid():N}"[..16]);
        var secondUserId = await CreateLocalUserAsync(admin, $"second-{Guid.NewGuid():N}"[..16]);
        _ = await admin.PostAsJsonAsync(
            $"/api/users/{firstUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));

        var conflict = await admin.PostAsJsonAsync(
            $"/api/users/{secondUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await conflict.Content.ReadFromJsonAsync<IdentityLinkErrorResponse>(JsonOptions);
        body!.Code.Should().Be(IdentityLinkErrorCodes.ExternalIdInUse);
    }

    [Fact]
    public async Task Add_entra_link_to_missing_user_returns_not_found()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/users/usrmissingentratest00001/identity-links",
            new AddIdentityLinkRequest("Entra", Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<IdentityLinkErrorResponse>(JsonOptions);
        body!.Code.Should().Be(IdentityLinkErrorCodes.UserNotFound);
    }

    [Fact]
    public async Task Caller_without_user_manage_gets_forbidden()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        var limitedUsername = $"limited-{Guid.NewGuid():N}"[..16];
        var limitedUserId = await CreateLocalUserAsync(admin, limitedUsername);
        var limitedClient = CreateTestClient(limitedUsername, "Local");

        var response = await limitedClient.PostAsJsonAsync(
            $"/api/users/{limitedUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Linked_entra_principal_inherits_platform_user_roles()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        var entraOid = Guid.NewGuid().ToString();
        var username = $"role-target-{Guid.NewGuid():N}"[..16];
        var targetUserId = await CreateLocalUserAsync(admin, username);
        var rolesResponse = await admin.GetAsync("/api/roles");
        rolesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await rolesResponse.Content.ReadFromJsonAsync<List<RoleListItem>>(JsonOptions);
        roles.Should().NotBeNullOrEmpty();
        var roleId = roles![0].Id;
        var assignRole = await admin.PostAsJsonAsync($"/api/users/{targetUserId}/roles", new { roleId });
        assignRole.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _ = await admin.PostAsJsonAsync(
            $"/api/users/{targetUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));

        var entraClient = CreateTestClient(entraOid, "Entra");
        var me = await entraClient.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        body!.Roles.Should().Contain(roleId);
    }

    [Fact]
    public async Task Admin_association_is_pending_until_the_operator_signs_in()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        var entraOid = Guid.NewGuid().ToString();
        var targetUserId = await CreateLocalUserAsync(admin, $"pending-{Guid.NewGuid():N}"[..16]);

        var added = await admin.PostAsJsonAsync(
            $"/api/users/{targetUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));
        added.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var link = await db.IdentityLinks.SingleAsync(l => l.ExternalId == entraOid).ConfigureAwait(false);
        link.LastAuthenticatedAt.Should().BeNull();
        link.LinkedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task First_entra_sign_in_proves_a_pending_link_and_disables_local_login()
    {
        AssertSqlAvailable();
        var admin = await EnsureAdminClientAsync();
        var entraOid = Guid.NewGuid().ToString();
        var username = $"proof-{Guid.NewGuid():N}"[..16];
        var targetUserId = await CreateLocalUserAsync(admin, username);
        _ = await admin.PostAsJsonAsync(
            $"/api/users/{targetUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));

        var entraClient = CreateTestClient(entraOid, "Entra");
        var me = await entraClient.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var link = await db.IdentityLinks.SingleAsync(l => l.ExternalId == entraOid).ConfigureAwait(false);
        link.LastAuthenticatedAt.Should().NotBeNull();

        var credential = await db.LocalCredentials.SingleAsync(c => c.UserId == targetUserId).ConfigureAwait(false);
        credential.IsDisabled.Should().BeTrue();
    }

    private sealed record RoleListItem(string Id, string Name, string? Description, bool IsSystemRole);

    [Fact]
    public async Task Sign_in_with_the_same_oid_from_another_tenant_does_not_resolve_the_user()
    {
        AssertSqlAvailable();
        await EnsureActiveRegistrationAsync();
        var admin = await EnsureAdminClientAsync();
        var entraOid = Guid.NewGuid().ToString();
        var targetUserId = await CreateLocalUserAsync(admin, $"xtenant-{Guid.NewGuid():N}"[..16]);
        var added = await admin.PostAsJsonAsync(
            $"/api/users/{targetUserId}/identity-links",
            new AddIdentityLinkRequest("Entra", entraOid));
        added.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var otherTenantClient = CreateTestClient(entraOid, "Entra", OtherTenantId);
        var otherTenantMe = await otherTenantClient.GetAsync("/api/auth/me");
        otherTenantMe.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var matchingClient = CreateTestClient(entraOid, "Entra", ActiveTenantId);
        var matchingMe = await matchingClient.GetAsync("/api/auth/me");
        matchingMe.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await matchingMe.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        body!.UserId.Should().Be(targetUserId);
    }

    private async Task EnsureActiveRegistrationAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var existing = await db.EntraInstanceRegistrations.FirstOrDefaultAsync(r => r.IsActive);
        if (existing is null)
        {
            db.EntraInstanceRegistrations.Add(new EntraInstanceRegistrationEntity
            {
                Id = "reg-iam5-http",
                DisplayName = "Integration directory",
                Authority = $"https://login.microsoftonline.com/{ActiveTenantId}/v2.0",
                ClientId = Guid.NewGuid().ToString(),
                Enabled = true,
                IsActive = true,
                AzureTenantId = ActiveTenantId,
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return;
        }

        existing.AzureTenantId = ActiveTenantId;
        existing.Enabled = true;
        existing.Authority = $"https://login.microsoftonline.com/{ActiveTenantId}/v2.0";
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> EnsureAdminClientAsync()
    {
        await EnsureActiveRegistrationAsync();
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        if (!await db.Users.AnyAsync().ConfigureAwait(false))
        {
            var bootstrap = _fixture.Factory.CreateClient();
            var created = await bootstrap.PostAsJsonAsync(
                "/api/bootstrap/first-admin",
                new FirstAdminRequest("Entra Link Admin", "Local", $"entra-admin-{Guid.NewGuid():N}"[..16], "unique-local-passphrase"));
            created.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
        }

        // Ordinary local users can sort ahead of the administrator. user.manage follows the platform-admin role.
        var platformAdminRoleId = BootstrapConstants.PlatformAdminRoleId;
        var link = await db.IdentityLinks
            .Where(l => l.Source == (int)IdentitySource.Local)
            .Where(l => db.UserRoles.Any(r => r.UserId == l.UserId && r.RoleId == platformAdminRoleId))
            .OrderBy(l => l.ExternalId)
            .FirstAsync()
            .ConfigureAwait(false);

        return CreateTestClient(link.ExternalId, "Local");
    }

    private async Task<string> CreateLocalUserAsync(HttpClient adminClient, string username)
    {
        var response = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest("Entra link test user", "Local", username, "unique-local-passphrase"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateUserResponse>(JsonOptions);
        return body!.UserId;
    }

    private HttpClient CreateTestClient(string externalId, string identitySource, string? tenantId = null)
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, externalId);
        if (!string.Equals(identitySource, "Local", StringComparison.OrdinalIgnoreCase))
            client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.IdentitySourceHeaderName, identitySource);
        if (string.Equals(identitySource, "Entra", StringComparison.OrdinalIgnoreCase))
        {
            client.DefaultRequestHeaders.Add(
                ManagementTestAuthHandler.TenantIdHeaderName,
                tenantId ?? ActiveTenantId);
        }

        return client;
    }
}
