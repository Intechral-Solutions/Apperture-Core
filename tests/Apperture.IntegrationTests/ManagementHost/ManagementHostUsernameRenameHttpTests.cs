using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// IAM-9.5 administrator rename through the real Management Host and SQL Server: <c>PUT /api/users/{id}/username</c>,
/// the ledger rules behind it (retired names stay bound to their user), the audit row, and that sign-in follows the
/// username while credentials, roles and an existing session are untouched.
/// </summary>
[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostUsernameRenameHttpTests
{
    private const string Password = "rename-test-passphrase-9204";
    private const string RecoveryPassword = "amber-Lantern-9204-plume-vortex";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostUsernameRenameHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    [Fact]
    public async Task Rename_requires_authentication_and_user_manage()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var target = await SeedAsync(tag + ".target");
        var nobody = await SeedAsync(tag + ".nobody");

        var anonymous = await _fixture.Factory!.CreateClient().PutAsJsonAsync($"/api/users/{target.Id.Value}/username", new RenameUsernameRequest(tag + ".new"));
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var forbidden = ClientFor(nobody.Username);
        var denied = await forbidden.PutAsJsonAsync($"/api/users/{target.Id.Value}/username", new RenameUsernameRequest(tag + ".new"));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CurrentUsernameAsync(target.Id)).Should().Be(target.Username, "a refused rename changes nothing");
    }

    [Fact]
    public async Task Rename_to_a_new_name_returns_200_and_moves_the_username_the_local_link_and_search_together()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var target = await SeedAsync(tag + ".before");
        using var admin = await AdminClientAsync();

        var response = await RenameAsync(admin, target.Id, "  " + tag.ToUpperInvariant() + ".After ");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<RenameUsernameResponse>(JsonOptions))!;
        body.UserId.Should().Be(target.Id.Value);
        body.Username.Should().Be(tag + ".after", "the canonical form is trimmed and lower-case");
        body.Outcome.Should().Be("assigned");

        var detail = (await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{target.Id.Value}", JsonOptions))!;
        detail.Username.Should().Be(tag + ".after");
        detail.IdentityLinks.Should().ContainSingle(l => l.Source == "Local" && l.ExternalId == tag + ".after", "the Local link follows the username");
        (await ListAsync(admin, tag + ".after")).Items.Should().ContainSingle(i => i.UserId == target.Id.Value);
        (await ListAsync(admin, tag + ".before")).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Rename_to_another_users_current_name_is_409_username_taken()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var target = await SeedAsync(tag + ".target");
        var other = await SeedAsync(tag + ".other");
        using var admin = await AdminClientAsync();

        var response = await RenameAsync(admin, target.Id, other.Username);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("username_taken");
        (await CurrentUsernameAsync(target.Id)).Should().Be(target.Username);
        (await CurrentUsernameAsync(other.Id)).Should().Be(other.Username);
    }

    [Fact]
    public async Task Rename_to_another_users_retired_name_is_409_username_taken_and_the_name_stays_bound()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var first = await SeedAsync(tag + ".first");
        var second = await SeedAsync(tag + ".second");
        using var admin = await AdminClientAsync();
        (await RenameAsync(admin, first.Id, tag + ".first-new")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await RenameAsync(admin, second.Id, tag + ".first");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("username_taken");
        (await LedgerOwnerAsync(tag + ".first")).Should().Be(first.Id.Value, "a retired name stays bound to the user who held it");
    }

    [Fact]
    public async Task Rename_back_to_the_users_own_retired_name_is_a_reclaim_with_200()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var person = await SeedAsync(tag + ".original");
        using var admin = await AdminClientAsync();
        (await RenameAsync(admin, person.Id, tag + ".interim")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await RenameAsync(admin, person.Id, tag + ".original");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<RenameUsernameResponse>(JsonOptions))!.Outcome.Should().Be("reclaimed");
        (await CurrentUsernameAsync(person.Id)).Should().Be(tag + ".original");
        (await LedgerOwnerAsync(tag + ".interim")).Should().Be(person.Id.Value, "the interim name is retired and stays bound to this user");
    }

    [Theory]
    [InlineData("system", "username_reserved")]
    [InlineData("breakglass", "username_reserved")]
    [InlineData("Bad Name!", "username_invalid")]
    [InlineData("ab", "username_invalid")]
    [InlineData("jane@contoso.com", "username_invalid")]
    public async Task Rename_to_a_name_that_fails_the_current_policy_is_400_with_its_code(string requested, string code)
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var person = await SeedAsync(tag + ".policy");
        using var admin = await AdminClientAsync();

        var response = await RenameAsync(admin, person.Id, requested);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(code);
        (await CurrentUsernameAsync(person.Id)).Should().Be(person.Username);
    }

    [Fact]
    public async Task A_grandfathered_name_can_be_renamed_away_from_but_never_reclaimed()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var grandfathered = tag + ".legacy@contoso.com";
        var person = await SeedGrandfatheredAsync(tag + ".legacy", grandfathered);
        using var admin = await AdminClientAsync();

        (await RenameAsync(admin, person.Id, tag + ".legacy")).StatusCode.Should().Be(HttpStatusCode.OK);
        var reclaim = await RenameAsync(admin, person.Id, grandfathered);

        reclaim.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the retired grandfathered name fails the current policy, so it cannot be reclaimed");
        (await reclaim.Content.ReadAsStringAsync()).Should().Contain("username_invalid");
        (await CurrentUsernameAsync(person.Id)).Should().Be(tag + ".legacy");
    }

    [Fact]
    public async Task Rename_of_recovery_access_is_409_unsafe_operation_and_changes_nothing()
    {
        AssertSqlAvailable();
        _ = await BootstrapHttpPlatformAdmin.EnsureLocalExternalIdAsync(_fixture.Factory!);
        UserId recoveryId;
        await using (var scope = _fixture.Factory!.Services.CreateAsyncScope())
            recoveryId = await scope.ServiceProvider.GetRequiredService<IEmergencyAccessService>().ProvisionAsync(RecoveryPassword, actorId: null);
        var before = await CurrentUsernameAsync(recoveryId);
        using var admin = await AdminClientAsync();

        var response = await RenameAsync(admin, recoveryId, "someone.else");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(EmergencyAccessErrorCodes.UnsafeOperation);
        (await CurrentUsernameAsync(recoveryId)).Should().Be(before);
    }

    [Fact]
    public async Task Rename_of_an_unknown_user_is_404()
    {
        AssertSqlAvailable();
        using var admin = await AdminClientAsync();

        (await RenameAsync(admin, new UserId("ffffffffffffffffffffffff"), "someone.else")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_successful_rename_writes_one_audit_row_with_the_previous_and_new_names_and_a_no_op_writes_none()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var person = await SeedAsync(tag + ".audited");
        using var admin = await AdminClientAsync();

        (await RenameAsync(admin, person.Id, tag + ".audited-new")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RenameAsync(admin, person.Id, tag + ".audited-new")).StatusCode.Should().Be(HttpStatusCode.OK, "renaming to the current name is a no-op");

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "governance.user.username.changed" && a.Details!.Contains(person.Id.Value))
            .ToListAsync();
        var row = rows.Should().ContainSingle().Which;
        row.Details.Should().Contain($"previousUsername={tag}.audited").And.Contain($"newUsername={tag}.audited-new");
    }

    [Fact]
    public async Task Rename_changes_no_password_role_or_other_user_and_keeps_an_existing_session_alive()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        var person = await SeedAsync(tag + ".session", withPassword: true);
        using var admin = await AdminClientAsync();
        var roleId = await CreateRoleAsync(admin, "Rename " + tag);
        (await admin.PostAsJsonAsync($"/api/users/{person.Id.Value}/roles", new AssignRoleRequest(roleId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var credentialBefore = await CredentialHashAsync(person.Id);

        var sessionClient = _fixture.Factory!.CreateClient();
        (await sessionClient.PostAsJsonAsync("/api/auth/local/login", new LocalLoginRequest(person.Username, Password))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RenameAsync(admin, person.Id, tag + ".renamed")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await CredentialHashAsync(person.Id)).Should().Be(credentialBefore, "rename touches no password");
        var detail = (await admin.GetFromJsonAsync<UserDetailDto>($"/api/users/{person.Id.Value}", JsonOptions))!;
        detail.RoleIds.Should().Equal(roleId);
        detail.UserId.Should().Be(person.Id.Value, "the stable identity is unchanged");

        var me = await sessionClient.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK, "sessions are keyed by user id, so a rename does not end them");
        (await me.Content.ReadFromJsonAsync<MeResponse>(JsonOptions))!.Username.Should().Be(tag + ".renamed");

        var newName = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/local/login", new LocalLoginRequest(tag + ".renamed", Password));
        newName.StatusCode.Should().Be(HttpStatusCode.OK, "local sign-in works with the new name");
        var oldName = await _fixture.Factory.CreateClient().PostAsJsonAsync("/api/auth/local/login", new LocalLoginRequest(person.Username, Password));
        oldName.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the old name no longer signs in");
    }

    // ---------------------------------------------------------------------------------------------------------------

    private static string NewTag() => "r" + Guid.NewGuid().ToString("N")[..10];

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, UserId id, string username) =>
        client.PutAsJsonAsync($"/api/users/{id.Value}/username", new RenameUsernameRequest(username));

    private static async Task<UserListResponse> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/users?q=" + Uri.EscapeDataString(query));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UserListResponse>(JsonOptions))!;
    }

    private static async Task<string> CreateRoleAsync(HttpClient admin, string name)
    {
        var created = await admin.PostAsJsonAsync("/api/roles", new CreateRoleRequest(name));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<RoleDetailDto>(JsonOptions))!.Id;
    }

    private async Task<HttpClient> AdminClientAsync() =>
        ClientFor(await BootstrapHttpPlatformAdmin.EnsureLocalExternalIdAsync(_fixture.Factory!));

    private HttpClient ClientFor(string localUsername)
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, localUsername);
        return client;
    }

    private async Task<User> SeedAsync(string username, bool withPassword = false)
    {
        // An ordinary user must never be the first platform user (that would close first-admin bootstrap).
        _ = await BootstrapHttpPlatformAdmin.EnsureLocalExternalIdAsync(_fixture.Factory!);

        var id = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            id,
            "Rename Test",
            [new IdentityLink(IdentitySource.Local, username, LastAuthenticatedAt: null)],
            [],
            false)
        { Username = username };

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user);
        if (withPassword)
        {
            var hasher = scope.ServiceProvider.GetRequiredService<ILocalPasswordHasher>();
            var (hash, salt) = hasher.HashPassword(Password);
            await scope.ServiceProvider.GetRequiredService<ILocalCredentialRepository>().SetPasswordAsync(id, hash, salt, hasher.AlgorithmName);
        }

        return user;
    }

    /// <summary>
    /// A user whose stored name predates the current policy. New users cannot be created with such a name, so the name is
    /// rewritten in place across <c>Users</c>, the ledger and the Local link, as the backfill leaves a legacy row.
    /// </summary>
    private async Task<User> SeedGrandfatheredAsync(string conformingName, string grandfatheredName)
    {
        var user = await SeedAsync(conformingName);
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var id = user.Id.Value;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [UsernameLedger] SET [Username] = {grandfatheredName} WHERE [Username] = {conformingName} AND [UserId] = {id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Users] SET [Username] = {grandfatheredName} WHERE [Id] = {id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [IdentityLinks] SET [ExternalId] = {grandfatheredName} WHERE [UserId] = {id} AND [Source] = {(int)IdentitySource.Local}");
        (await CurrentUsernameAsync(user.Id)).Should().Be(grandfatheredName);
        return user with { Username = grandfatheredName };
    }

    private async Task<string> CurrentUsernameAsync(UserId id)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Id == id.Value).Select(u => u.Username).SingleAsync();
    }

    private async Task<string?> LedgerOwnerAsync(string username)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.UsernameLedger.AsNoTracking().Where(l => l.Username == username).Select(l => l.UserId).SingleOrDefaultAsync();
    }

    private async Task<string?> CredentialHashAsync(UserId id)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var credential = await scope.ServiceProvider.GetRequiredService<ILocalCredentialRepository>().GetByUserIdAsync(id);
        return credential?.PasswordHash;
    }
}
