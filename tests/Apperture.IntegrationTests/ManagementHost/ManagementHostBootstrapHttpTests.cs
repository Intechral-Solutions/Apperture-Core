using System.Net;
using System.Net.Http.Json;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostBootstrapHttpTests
{
    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostBootstrapHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A01_GetStatus_before_setup_reports_uninitialized()
    {
        AssertSqlAvailable();
        await EnsureNoUsersAsync();

        var client = _fixture.Factory!.CreateClient();
        var response = await client.GetAsync("/api/bootstrap/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BootstrapStatusResponse>();
        body.Should().NotBeNull();
        body!.Initialized.Should().BeFalse();
        body.AllowsFirstAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task A02_Post_first_admin_before_setup_creates_administrator()
    {
        AssertSqlAvailable();
        await EnsureNoUsersAsync();

        var client = _fixture.Factory!.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/bootstrap/first-admin",
            new FirstAdminRequest("Bootstrap Admin", "Local", $"bootstrap-admin-{Guid.NewGuid():N}"[..20], "unique-local-passphrase"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<FirstAdminResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A03_GetStatus_after_setup_reports_initialized()
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();
        var response = await client.GetAsync("/api/bootstrap/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BootstrapStatusResponse>();
        body.Should().NotBeNull();
        body!.Initialized.Should().BeTrue();
        body.AllowsFirstAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task A04_Post_first_admin_after_setup_returns_conflict_with_stable_code()
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();
        await EnsureInitializedAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/bootstrap/first-admin",
            new FirstAdminRequest("Another Admin", "Local", "another-admin", "unique-local-passphrase"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<BootstrapErrorResponse>();
        body.Should().NotBeNull();
        body!.Code.Should().Be(BootstrapErrorCodes.AlreadyInitialized);
        body.Initialized.Should().BeTrue();
        body.Message.Should().Contain("already initialized");
    }

    [Fact]
    public async Task A05_Bootstrap_attempts_emit_audit_events()
    {
        AssertSqlAvailable();
        var client = _fixture.Factory!.CreateClient();
        await EnsureInitializedAsync(client);
        _ = await client.PostAsJsonAsync(
            "/api/bootstrap/first-admin",
            new FirstAdminRequest("Audit Retry", "Local", "audit-retry-admin", "unique-local-passphrase"));

        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var page = await audit.QueryAsync(new AuditQuery(FilterByActionPrefix: "bootstrap.first_admin"), CancellationToken.None);
        page.Items.Select(r => r.Action).Should().Contain("bootstrap.first_admin.rejected_already_initialized");
    }

    private static async Task EnsureInitializedAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<BootstrapStatusResponse>("/api/bootstrap/status");
        if (status is { Initialized: true })
            return;

        var create = await client.PostAsJsonAsync(
            "/api/bootstrap/first-admin",
            new FirstAdminRequest("Bootstrap Admin", "Local", $"bootstrap-admin-{Guid.NewGuid():N}"[..20], "unique-local-passphrase"));
        create.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    private async Task EnsureNoUsersAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        if (!await db.Users.AnyAsync())
            return;

        db.UserRoles.RemoveRange(db.UserRoles);
        db.IdentityLinks.RemoveRange(db.IdentityLinks);
        db.LocalCredentials.RemoveRange(db.LocalCredentials);
        // Recovery Access tests in this collection enroll an authenticator.
        db.UserMfaRecoveryCodes.RemoveRange(db.UserMfaRecoveryCodes);
        db.UserTotpCredentials.RemoveRange(db.UserTotpCredentials);
        // Test-only reset: the product never deletes users, so the ledger restricts it.
        db.UsernameLedger.RemoveRange(db.UsernameLedger);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();
    }
}
