using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// IAM-9 gate §22: Entra provisioning results carry the assigned <c>Username</c>. These tests run the real Management
/// Host against SQL Server and compare every reported username with the value read back from the <c>Users</c> table,
/// so a pass cannot come from a skipped or faked persistence layer (the class fails, never skips, without SQL).
/// </summary>
[Collection(BootstrapHttpCollection.Name)]
public sealed class ManagementHostEntraProvisioningUsernameHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly BootstrapHttpFixture _fixture;

    public ManagementHostEntraProvisioningUsernameHttpTests(BootstrapHttpFixture fixture) => _fixture = fixture;

    private void AssertSqlAvailable()
    {
        if (_fixture.ConnectionString is null || _fixture.Factory is null)
            throw new InvalidOperationException(SqlServerTestConfiguration.DescribeActiveConfiguration());
    }

    [Fact]
    public async Task Approving_an_access_request_returns_the_username_stored_for_the_new_user()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        await EnsureAdministratorAsync();
        var request = NewRequest(tag, upn: $"{tag}.jane@contoso.com");
        await SeedRequestAsync(request);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(ApprovePath(request.Id), new EntraAccessRequestDecisionBody(null, false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(raw).RootElement.GetProperty("username").GetString().Should().Be($"{tag}.jane", "the wire contract names the field username");
        var body = JsonSerializer.Deserialize<EntraProvisioningPersonResultDto>(raw, JsonOptions)!;
        body.Succeeded.Should().BeTrue();
        body.UserId.Should().NotBeNullOrEmpty();
        body.Username.Should().Be($"{tag}.jane", "generated from the UPN snapshot, never the object id or the email");
        (await StoredUsernameAsync(body.UserId!)).Should().Be(body.Username, "the response reports the stored username");
        (await LedgerOwnerAsync(body.Username!)).Should().Be(body.UserId);
    }

    [Fact]
    public async Task A_taken_base_name_is_suffixed_and_the_response_reports_the_stored_suffixed_name()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        await EnsureAdministratorAsync();
        await SeedLocalUserAsync($"{tag}.jane");
        var request = NewRequest(tag, upn: $"{tag}.jane@contoso.com");
        await SeedRequestAsync(request);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(ApprovePath(request.Id), new EntraAccessRequestDecisionBody(null, false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<EntraProvisioningPersonResultDto>(JsonOptions))!;
        body.Username.Should().Be($"{tag}.jane-2", "the base name is held by another user, so the stored name is the next candidate");
        (await StoredUsernameAsync(body.UserId!)).Should().Be(body.Username);
    }

    [Fact]
    public async Task Approving_a_request_for_an_identity_that_is_already_a_user_is_409_and_names_no_username()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        await EnsureAdministratorAsync();
        var request = NewRequest(tag, upn: $"{tag}.sam@contoso.com");
        var existing = await SeedEntraUserAsync($"{tag}.existing", request.TenantId, request.ProviderObjectId);
        await SeedRequestAsync(request);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(ApprovePath(request.Id), new EntraAccessRequestDecisionBody(null, false));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "an existing identity keeps its existing conflict outcome");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("code").GetString().Should().Be(UserProvisioningErrorCodes.DuplicateIdentity);
        json.TryGetProperty("username", out _).Should().BeFalse("the conflict body identifies no created user");
        (await StoredRequestAsync(request.Id)).ProvisionedUserId.Should().Be(existing.Id.Value, "the request is still reconciled to the existing user");
        (await UserCountForObjectAsync(request.TenantId, request.ProviderObjectId)).Should().Be(1, "no second user is created");
    }

    [Fact]
    public async Task Batch_provisioning_reports_the_stored_username_per_person_and_none_for_failures()
    {
        AssertSqlAvailable();
        var tag = NewTag();
        await EnsureAdministratorAsync();
        var tenant = Guid.NewGuid().ToString();
        var registrationId = await SeedRegistrationAsync(tag, tenant);
        var ok = Guid.NewGuid().ToString();
        var disabled = Guid.NewGuid().ToString();
        var already = Guid.NewGuid().ToString();
        var missing = Guid.NewGuid().ToString();
        await SeedEntraUserAsync($"{tag}.already", tenant, already);
        var directory = new StubDirectory(
            DirectoryUser(ok, $"{tag}.ada@contoso.com", enabled: true),
            DirectoryUser(disabled, $"{tag}.off@contoso.com", enabled: false),
            DirectoryUser(already, $"{tag}.dup@contoso.com", enabled: true));
        using var factory = _fixture.Factory!.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEntraProvisioningDirectoryService>();
            services.AddSingleton<IEntraProvisioningDirectoryService>(directory);
        }));
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, await _fixture.Factory!.AdminExternalIdAsync());

        var response = await admin.PostAsJsonAsync(
            "/api/users/entra",
            new EntraDirectoryProvisionRequestBody(registrationId, [ok, disabled, already, missing], null, false));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "per-person failures stay in the batch result");
        var raw = await response.Content.ReadAsStringAsync();
        var results = JsonSerializer.Deserialize<EntraProvisioningBatchResultDto>(raw, JsonOptions)!.Results;
        results.Select(r => r.ObjectId).Should().Equal(ok, disabled, already, missing);

        var created = results[0];
        created.Succeeded.Should().BeTrue();
        created.Username.Should().Be($"{tag}.ada");
        (await StoredUsernameAsync(created.UserId!)).Should().Be(created.Username, "the batch reports the stored username");

        results.Skip(1).Should().OnlyContain(r => !r.Succeeded && r.UserId == null && r.Username == null,
            "failures identify no user, so they carry no username");
        results[1].ErrorCode.Should().Be(UserProvisioningErrorCodes.DirectoryUserDisabled);
        results[2].ErrorCode.Should().Be(UserProvisioningErrorCodes.DuplicateIdentity);
        results[3].ErrorCode.Should().Be(UserProvisioningErrorCodes.DirectoryUserMissing);

        var rows = JsonDocument.Parse(raw).RootElement.GetProperty("results").EnumerateArray().ToList();
        rows[0].GetProperty("username").GetString().Should().Be($"{tag}.ada");
        rows.Skip(1).Select(HasUsername).Should().OnlyContain(has => !has, "failed rows name no username");
    }

    // ---------------------------------------------------------------------------------------------------------------

    private static string NewTag() => "p" + Guid.NewGuid().ToString("N")[..10];

    private static bool HasUsername(JsonElement row) =>
        row.TryGetProperty("username", out var username) && username.ValueKind != JsonValueKind.Null;

    private static string ApprovePath(string requestId) => $"/api/users/entra/access-requests/{requestId}/approve";

    private static EntraAccessRequest NewRequest(string tag, string upn) =>
        new(
            "rq-" + tag,
            "reg-" + tag,
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "Jane Smith",
            upn,
            upn,
            "Member",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(-4),
            EntraAccessRequestStatus.Pending);

    private static EntraProvisioningDirectoryUser DirectoryUser(string objectId, string upn, bool enabled) =>
        new(objectId, "Directory Person", upn, upn, "Member", enabled, EntraDirectoryAppertureStatus.Available, null);

    private async Task EnsureAdministratorAsync() => _ = await _fixture.Factory!.AdminExternalIdAsync();

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _fixture.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, await _fixture.Factory!.AdminExternalIdAsync());
        return client;
    }

    private async Task SeedRequestAsync(EntraAccessRequest request)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEntraAccessRequestRepository>().AddAsync(request);
    }

    private async Task<string> SeedRegistrationAsync(string tag, string tenantId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var added = await scope.ServiceProvider.GetRequiredService<IEntraInstanceRegistrationRepository>().AddAsync(
            new EntraInstanceRegistration
            {
                Id = "reg-" + tag,
                DisplayName = "Contoso " + tag,
                Authority = "https://login.microsoftonline.com/" + tenantId,
                ClientId = "client-" + tag,
                AzureTenantId = tenantId,
                Enabled = true,
                IsActive = false
            });
        return added.Id;
    }

    private async Task<User> SeedLocalUserAsync(string username)
    {
        var user = new User(
            NewUserId(),
            "Existing Local",
            [new IdentityLink(IdentitySource.Local, username, LastAuthenticatedAt: null)],
            [],
            false)
        { Username = username };
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }

    private async Task<User> SeedEntraUserAsync(string username, string tenantId, string objectId)
    {
        var user = new User(
            NewUserId(),
            "Existing Entra",
            [new IdentityLink(IdentitySource.Entra, objectId, LastAuthenticatedAt: null, TenantId: tenantId)],
            [],
            false)
        { Username = username };
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user);
        return user;
    }

    private static UserId NewUserId() => new(Guid.NewGuid().ToString("N")[..24]);

    private async Task<string> StoredUsernameAsync(string userId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Username).SingleAsync();
    }

    private async Task<string?> LedgerOwnerAsync(string username)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.UsernameLedger.AsNoTracking().Where(l => l.Username == username).Select(l => l.UserId).SingleOrDefaultAsync();
    }

    private async Task<EntraAccessRequest> StoredRequestAsync(string requestId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IEntraAccessRequestRepository>().GetByIdAsync(requestId))!;
    }

    private async Task<int> UserCountForObjectAsync(string tenantId, string objectId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.IdentityLinks.AsNoTracking()
            .CountAsync(l => l.Source == (int)IdentitySource.Entra && l.TenantId == tenantId && l.ExternalId == objectId);
    }

    private sealed class StubDirectory : IEntraProvisioningDirectoryService
    {
        private readonly Dictionary<string, EntraProvisioningDirectoryUser> _users;

        public StubDirectory(params EntraProvisioningDirectoryUser[] users) =>
            _users = users.ToDictionary(u => u.ObjectId, StringComparer.OrdinalIgnoreCase);

        public ValueTask<EntraProvisioningDirectoryPage> SearchAsync(
            string registrationId, string? search, string? continuationToken, int top, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EntraProvisioningDirectoryUser?> GetByObjectIdAsync(
            string registrationId, string objectId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_users.GetValueOrDefault(objectId));
    }
}

internal static class ManagementHostFactoryAdminExtensions
{
    /// <summary>The local sign-in name of the shared platform administrator, created on first use.</summary>
    public static Task<string> AdminExternalIdAsync(this ManagementHostWebApplicationFactory factory) =>
        BootstrapHttpPlatformAdmin.EnsureLocalExternalIdAsync(factory);
}
