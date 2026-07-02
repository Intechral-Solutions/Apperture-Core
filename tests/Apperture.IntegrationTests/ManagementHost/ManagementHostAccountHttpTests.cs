using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostAccountHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostAccountHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    [Fact]
    public async Task Get_me_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_me_unlinked_authenticated_principal_returns_403()
    {
        AssertSqlAvailable();
        await EnsureFirstAdminAsync();

        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, "not-linked-external-id");
        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_me_for_linked_local_user_returns_enriched_account_fields()
    {
        AssertSqlAvailable();
        var (client, username) = await CreateAuthenticatedClientForFirstAdminAsync();

        var response = await client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.UserId.Should().NotBeNullOrWhiteSpace();
        body.DisplayName.Should().NotBeNullOrWhiteSpace();
        body.SignInSource.Should().Be("Local");
        body.LocalCredentialStatus.Should().Be(AccountMeSnapshotBuilder.LocalCredentialStatusActive);
        body.CanChangeLocalPassword.Should().BeTrue();
        body.IdentityLinks.Should().Contain(l => l.Source == "Local" && l.DisplayLabel == username);
        body.Deferred.AvatarManagementDeferred.Should().BeTrue();
    }

    [Fact]
    public async Task Change_password_success_allows_login_with_new_password()
    {
        AssertSqlAvailable();
        var (client, username) = await CreateAuthenticatedClientForFirstAdminAsync();
        const string newPassword = "NewPassword9!";

        var change = await client.PostAsJsonAsync(
            "/api/auth/local/change-password",
            new ChangeLocalPasswordRequest("Password1!", newPassword));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var loginClient = _fixture.Factory!.CreateClient();
        var loginOld = await loginClient.PostAsJsonAsync(
            "/api/auth/local/login",
            new LocalLoginRequest(username, "Password1!"));
        loginOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var loginNew = await loginClient.PostAsJsonAsync(
            "/api/auth/local/login",
            new LocalLoginRequest(username, newPassword));
        loginNew.StatusCode.Should().Be(HttpStatusCode.OK);

        await client.PostAsJsonAsync(
            "/api/auth/local/change-password",
            new ChangeLocalPasswordRequest(newPassword, "Password1!"));
    }

    [Fact]
    public async Task Change_password_wrong_current_returns_bad_request_with_code()
    {
        AssertSqlAvailable();
        var (client, _) = await CreateAuthenticatedClientForFirstAdminAsync();

        var change = await client.PostAsJsonAsync(
            "/api/auth/local/change-password",
            new ChangeLocalPasswordRequest("wrong-password", "NewPassword9!"));
        change.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await change.Content.ReadFromJsonAsync<LocalPasswordChangeErrorResponse>(JsonOptions);
        body!.Code.Should().Be(LocalPasswordChangeErrorCodes.WrongCurrentPassword);
    }

    [Fact]
    public async Task Change_password_without_auth_returns_401()
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();
        var change = await client.PostAsJsonAsync(
            "/api/auth/local/change-password",
            new ChangeLocalPasswordRequest("Password1!", "NewPassword9!"));
        change.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_emits_audit_on_success()
    {
        AssertSqlAvailable();
        var (client, _) = await CreateAuthenticatedClientForFirstAdminAsync();

        _ = await client.PostAsJsonAsync(
            "/api/auth/local/change-password",
            new ChangeLocalPasswordRequest("Password1!", "AuditPassword9!"));

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var page = await audit.QueryAsync(
            new AuditQuery(FilterByActionPrefix: "auth.local.password_change"),
            CancellationToken.None);
        page.Items.Select(r => r.Action).Should().Contain("auth.local.password_change.success");

        await client.PostAsJsonAsync(
            "/api/auth/local/change-password",
            new ChangeLocalPasswordRequest("AuditPassword9!", "Password1!"));
    }

    private async Task EnsureFirstAdminAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        if (await db.Users.AnyAsync().ConfigureAwait(false))
            return;

        var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/bootstrap/first-admin",
            new FirstAdminRequest("Account Test Admin", "Local", $"acct-admin-{Guid.NewGuid():N}"[..16], "Password1!"));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    private async Task<(HttpClient Client, string Username)> CreateAuthenticatedClientForFirstAdminAsync()
    {
        await EnsureFirstAdminAsync();

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var localLink = await db.IdentityLinks
            .Where(l => l.Source == (int)IdentitySource.Local)
            .OrderBy(l => l.ExternalId)
            .FirstAsync()
            .ConfigureAwait(false);

        await EnsurePasswordIsAsync(localLink.ExternalId, "Password1!");

        var client = _fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, localLink.ExternalId);
        return (client, localLink.ExternalId);
    }

    private async Task EnsurePasswordIsAsync(string username, string expectedPassword)
    {
        var loginClient = _fixture.Factory!.CreateClient();
        var login = await loginClient.PostAsJsonAsync(
            "/api/auth/local/login",
            new LocalLoginRequest(username, expectedPassword));
        if (login.StatusCode == HttpStatusCode.OK)
            return;

        foreach (var alternate in new[] { "NewPassword9!", "AuditPassword9!" })
        {
            var recovered = await loginClient.PostAsJsonAsync(
                "/api/auth/local/login",
                new LocalLoginRequest(username, alternate));
            if (recovered.StatusCode != HttpStatusCode.OK)
                continue;

            var authClient = _fixture.Factory.CreateClient();
            authClient.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, username);
            _ = await authClient.PostAsJsonAsync(
                "/api/auth/local/change-password",
                new ChangeLocalPasswordRequest(alternate, expectedPassword));
            return;
        }

        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
