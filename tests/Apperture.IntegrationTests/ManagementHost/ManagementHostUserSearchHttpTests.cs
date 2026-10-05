using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// IAM-9.3 through the real Management Host: <c>GET /api/users?q=</c> search, the username on user DTOs and
/// <c>/me</c>, and Local user creation and Local identity linking by username.
/// </summary>
[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostUserSearchHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostUserSearchHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    [Fact]
    public async Task Listing_users_without_authentication_is_401_and_without_user_read_is_403()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var nobody = await SeedAsync(tag + ".nobody", "No Permissions");

        (await _fixture.Factory!.CreateClient().GetAsync("/api/users?q=" + tag)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var forbidden = await ClientForAsync(nobody.Username);
        (await forbidden.GetAsync("/api/users?q=" + tag)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_matches_username_prefix_and_display_name_substring_with_exact_username_first()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        // "{tag}" itself is not a legal assignment, so the exact-match user is the bare tag-shaped name.
        await SeedAsync(tag, "Exact Holder");
        await SeedAsync(tag + ".zed", "Zed Prefix");
        await SeedAsync("aaa." + tag, "The " + tag.ToUpperInvariant() + " Fan");
        await SeedAsync("unrelated." + tag[..6], "Someone Else");
        using var admin = await AdminClientAsync();

        var result = await ListAsync(admin, $"?q={tag.ToUpperInvariant()}");

        // Exact username first, then the rest by username ascending: the display-name match "aaa.*" sorts before ".zed".
        result.Items.Select(i => i.Username).Should().Equal(tag, "aaa." + tag, tag + ".zed");
        result.TotalCount.Should().Be(3);
        result.Items[0].DisplayName.Should().Be("Exact Holder");
        result.Items[0].UserId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Search_pages_with_a_total_and_clamps_take_to_at_least_one()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        await SeedAsync(tag + ".a", "A");
        await SeedAsync(tag + ".b", "B");
        await SeedAsync(tag + ".c", "C");
        using var admin = await AdminClientAsync();

        var second = await ListAsync(admin, $"?q={tag}&skip=1&take=1");
        second.Items.Select(i => i.Username).Should().Equal(tag + ".b");
        second.TotalCount.Should().Be(3);

        var clampedUp = await ListAsync(admin, $"?q={tag}&take=0");
        clampedUp.Items.Should().HaveCount(1);

        var clampedDown = await ListAsync(admin, $"?q={tag}&take=100000");
        clampedDown.Items.Should().HaveCount(3);

        var defaulted = await ListAsync(admin, $"?q={tag}");
        defaulted.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Search_treats_wildcards_literally_and_does_not_match_email_or_user_id()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var person = await SeedAsync(tag + ".one", "Percent " + tag + "%", email: tag + "@mail.example");
        await SeedAsync(tag + ".two", "Plain " + tag);
        using var admin = await AdminClientAsync();

        (await ListAsync(admin, $"?q={Uri.EscapeDataString(tag + "%")}")).Items.Select(i => i.Username).Should().Equal(tag + ".one");
        (await ListAsync(admin, $"?q={Uri.EscapeDataString(tag + "_")}")).Items.Should().BeEmpty();
        (await ListAsync(admin, $"?q={Uri.EscapeDataString("@mail.example")}")).Items.Should().BeEmpty();
        (await ListAsync(admin, $"?q={person.Id.Value}")).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_excludes_recovery_access()
    {
        AssertSqlAvailable();
        using var admin = await AdminClientAsync();

        (await ListAsync(admin, "?q=breakglass")).Items.Should().NotContain(i => i.Username == "breakglass");
        (await ListAsync(admin, "?take=100")).Items.Should().NotContain(i => i.Username == "breakglass");
    }

    [Fact]
    public async Task Search_terms_are_never_written_to_the_audit_log()
    {
        AssertSqlAvailable();
        var term = "auditprobe" + NewTag();
        using var admin = await AdminClientAsync();

        (await admin.GetAsync("/api/users?q=" + term)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        (await db.AuditLogs.AsNoTracking().CountAsync(a => a.Details != null && a.Details.Contains(term))).Should().Be(0);
        (await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action.Contains(term))).Should().Be(0);
    }

    [Fact]
    public async Task User_detail_and_me_expose_the_current_username()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var person = await SeedAsync(tag + ".detail", "Detail Person");
        using var admin = await AdminClientAsync();

        var detail = await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{person.Id.Value}", JsonOptions);
        detail!.Username.Should().Be(person.Username);

        using var self = await ClientForAsync(person.Username);
        var me = await self.GetFromJsonAsync<MeResponse>("/api/auth/me", JsonOptions);
        me!.Username.Should().Be(person.Username);
        me.UserPrincipal.Should().Be(person.Username);
    }

    [Fact]
    public async Task Create_local_user_by_username_by_alias_and_with_a_conflict()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        using var admin = await AdminClientAsync();

        var byUsername = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "By Username", "Local", Password: "a-sturdy-passphrase-1", Username: tag + ".preferred"));
        byUsername.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdId = (await byUsername.Content.ReadFromJsonAsync<CreateUserResponse>(JsonOptions))!.UserId;
        var detail = (await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{createdId}", JsonOptions))!;
        detail.Username.Should().Be(tag + ".preferred");
        detail.IdentityLinks.Should().ContainSingle(l => l.Source == "Local" && l.ExternalId == tag + ".preferred");

        var byAlias = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "By Alias", "Local", tag + ".alias", "a-sturdy-passphrase-1"));
        byAlias.StatusCode.Should().Be(HttpStatusCode.Created);

        var mismatch = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Conflicting", "Local", tag + ".one", "a-sturdy-passphrase-1", Username: tag + ".two"));
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadAsStringAsync()).Should().Contain("username_mismatch");
        (await ListAsync(admin, $"?q={tag}.one")).Items.Should().BeEmpty();
        (await ListAsync(admin, $"?q={tag}.two")).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Local_identity_link_may_omit_the_name_and_must_match_the_username()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var person = await SeedAsync(tag + ".linked", "Linked Person");
        using var admin = await AdminClientAsync();

        var omitted = await admin.PostAsJsonAsync($"/api/users/{person.Id.Value}/identity-links", new AddIdentityLinkRequest("Local", null));
        omitted.StatusCode.Should().Be(HttpStatusCode.NoContent, "the user already has exactly this Local link, so this is a no-op");

        var wrong = await admin.PostAsJsonAsync($"/api/users/{person.Id.Value}/identity-links", new AddIdentityLinkRequest("Local", tag + ".other"));
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("username_mismatch");

        var detail = (await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{person.Id.Value}", JsonOptions))!;
        detail.IdentityLinks.Where(l => l.Source == "Local").Should().ContainSingle();

        var entra = await admin.PostAsJsonAsync($"/api/users/{person.Id.Value}/identity-links", new AddIdentityLinkRequest("Entra", null));
        entra.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await entra.Content.ReadAsStringAsync()).Should().Contain("missing_external_id");
    }

    // ---------------------------------------------------------------------------------------------------------------

    private static string NewTag() => "s" + Guid.NewGuid().ToString("N")[..10];

    private async Task<UserListResponse> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/users" + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UserListResponse>(JsonOptions))!;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var admin = await BootstrapHttpPlatformAdmin.EnsureLocalExternalIdAsync(_fixture.Factory!);
        return await ClientForAsync(admin);
    }

    private Task<HttpClient> ClientForAsync(string localUsername)
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, localUsername);
        return Task.FromResult(client);
    }

    private async Task<User> SeedAsync(string username, string displayName, string? email = null)
    {
        // An ordinary user must never be the first platform user (that would close first-admin bootstrap).
        _ = await BootstrapHttpPlatformAdmin.EnsureLocalExternalIdAsync(_fixture.Factory!);

        var id = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            id,
            displayName,
            [new IdentityLink(IdentitySource.Local, username, LastAuthenticatedAt: null)],
            [],
            false,
            EmailAddress: email,
            EmailVerifiedAtUtc: email is null ? null : DateTimeOffset.UtcNow)
        { Username = username };

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }
}
