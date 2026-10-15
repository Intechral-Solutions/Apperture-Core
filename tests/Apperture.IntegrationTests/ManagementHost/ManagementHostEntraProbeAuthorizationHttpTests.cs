using System.Net;
using System.Net.Http.Json;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.IntegrationTests.Audit;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// SEC-2.4 (acceptance A4.4) on the real Management.Host pipeline: who may run an Entra probe is unchanged, and a refused or
/// unauthenticated request neither reaches the probe nor updates the registration's telemetry.
/// <list type="bullet">
/// <item><c>auth.configuration.read</c> keeps the directory searches (users and groups) and is refused <c>validate-openid</c>.</item>
/// <item><c>auth.entra.manage</c> keeps <c>validate-openid</c> and is accepted for the searches.</item>
/// <item>A principal with neither gets 403 on all three, an unauthenticated caller 401, and no probe runs.</item>
/// <item>A search by an authorized reader records its outcome on the diagnostics row and leaves the registration row, including
/// its <c>rowversion</c> and <c>UpdatedUtc</c>, as it was.</item>
/// </list>
/// </summary>
[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class ManagementHostEntraProbeAuthorizationHttpTests
{
    private readonly ManagementHostGovernanceFixture _fixture;

    public ManagementHostEntraProbeAuthorizationHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    private const string ReadPermission = "auth.configuration.read";
    private const string ManagePermission = "auth.entra.manage";

    [Fact]
    public async Task A_reader_may_search_the_directory_and_the_search_updates_telemetry_only()
    {
        RequireSql();
        var reader = await AddPrincipalAsync("sec24-reader", ReadPermission);
        var registrationId = await AddRegistrationAsync();
        var before = await RegistrationRowAsync(registrationId);
        var discovery = new Audit.EntraRegistrationSqlHarness.ScriptedDiscovery();
        using var factory = WithProbes(discovery, new Audit.EntraRegistrationSqlHarness.ScriptedValidator());

        foreach (var kind in new[] { "users", "groups" })
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, reader);
            var response = await client.PostAsJsonAsync($"/api/config/auth/entra-tenants/{registrationId}/directory/{kind}", new { search = "dana", top = 5 });
            response.StatusCode.Should().Be(HttpStatusCode.OK, kind);
        }

        discovery.Calls.Should().Be(2);
        var diagnostics = await DiagnosticsAsync(registrationId);
        diagnostics.Should().NotBeNull("the first search created the registration's telemetry row");
        diagnostics!.DirectoryDiscoveryStatus.Should().Be((int)DirectoryDiscoveryStatus.Success);
        diagnostics.LastDirectoryDiscoveryUtc.Should().NotBeNull();
        diagnostics.OpenIdConfigurationStatus.Should().Be((int)OpenIdConfigurationStatus.NotValidated, "no validation ran");

        var after = await RegistrationRowAsync(registrationId);
        after.RowVersion.Should().Equal(before.RowVersion, "a search never replaces the token the edit form checks");
        after.UpdatedUtc.Should().Be(before.UpdatedUtc);
        after.DisplayName.Should().Be(before.DisplayName);

        // The registration the detail route returns carries the telemetry the search recorded.
        using var detail = _fixture.CreateAuthenticatedClient(reader);
        var shown = await detail.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/config/auth/entra-tenants/{registrationId}");
        shown.GetProperty("directoryDiscoveryStatus").GetString().Should().Be(nameof(DirectoryDiscoveryStatus.Success));
    }

    [Fact]
    public async Task A_reader_is_refused_the_openid_validation_and_it_neither_runs_nor_updates_telemetry()
    {
        RequireSql();
        var reader = await AddPrincipalAsync("sec24-reader-v", ReadPermission);
        var registrationId = await AddRegistrationAsync();
        var validator = new Audit.EntraRegistrationSqlHarness.ScriptedValidator();
        using var factory = WithProbes(new Audit.EntraRegistrationSqlHarness.ScriptedDiscovery(), validator);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, reader);
        (await client.PostAsync($"/api/config/auth/entra-tenants/{registrationId}/validate-openid", content: null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        validator.Calls.Should().Be(0);
        (await DiagnosticsAsync(registrationId)).Should().BeNull("a refused request records nothing");
    }

    [Fact]
    public async Task A_manager_keeps_the_openid_validation_and_is_accepted_for_the_searches()
    {
        RequireSql();
        var manager = await AddPrincipalAsync("sec24-manager", ManagePermission);
        var registrationId = await AddRegistrationAsync();
        var validator = new Audit.EntraRegistrationSqlHarness.ScriptedValidator();
        var discovery = new Audit.EntraRegistrationSqlHarness.ScriptedDiscovery();
        using var factory = WithProbes(discovery, validator);
        var before = await RegistrationRowAsync(registrationId);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, manager);
        (await client.PostAsync($"/api/config/auth/entra-tenants/{registrationId}/validate-openid", content: null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/config/auth/entra-tenants/{registrationId}/directory/users", new { search = "dana", top = 5 })).StatusCode.Should().Be(HttpStatusCode.OK);

        validator.Calls.Should().Be(1);
        discovery.Calls.Should().Be(1);
        var diagnostics = (await DiagnosticsAsync(registrationId))!;
        diagnostics.OpenIdConfigurationStatus.Should().Be((int)OpenIdConfigurationStatus.Valid);
        diagnostics.DirectoryDiscoveryStatus.Should().Be((int)DirectoryDiscoveryStatus.Success);
        var after = await RegistrationRowAsync(registrationId);
        after.RowVersion.Should().Equal(before.RowVersion);
        after.UpdatedUtc.Should().Be(before.UpdatedUtc);
    }

    [Fact]
    public async Task A_principal_without_either_permission_gets_403_and_an_unauthenticated_caller_401_and_no_probe_runs()
    {
        RequireSql();
        var nobody = await AddPrincipalAsync("sec24-nobody", permission: null);
        var registrationId = await AddRegistrationAsync();
        var validator = new Audit.EntraRegistrationSqlHarness.ScriptedValidator();
        var discovery = new Audit.EntraRegistrationSqlHarness.ScriptedDiscovery();
        using var factory = WithProbes(discovery, validator);

        var paths = new[] { "directory/users", "directory/groups", "validate-openid" };
        foreach (var path in paths)
        {
            var url = $"/api/config/auth/entra-tenants/{registrationId}/{path}";
            HttpContent? Body() => path == "validate-openid" ? null : JsonContent.Create(new { search = "dana", top = 5 });

            using var refused = factory.CreateClient();
            refused.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, nobody);
            (await refused.PostAsync(url, Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);

            using var anonymous = factory.CreateClient();
            (await anonymous.PostAsync(url, Body())).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
        }

        validator.Calls.Should().Be(0);
        discovery.Calls.Should().Be(0);
        (await DiagnosticsAsync(registrationId)).Should().BeNull("a refused or unauthenticated request records nothing");
    }

    // ---- helpers ----

    private void RequireSql() =>
        _fixture.SqlAvailable.Should().BeTrue("the fixture fails hard: " + SqlServerTestConfiguration.DescribeActiveConfiguration());

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Apperture.Management.Host.ManagementHostWebApplicationFactoryEntryPoint> WithProbes(
        IEntraDirectoryDiscoveryService discovery, IEntraOpenIdConnectConfigurationValidator validator) =>
        _fixture.Factory!.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(discovery);
            services.AddSingleton(validator);
        }));

    /// <summary>A local principal holding exactly the given permission on the instance configuration (or none).</summary>
    private async Task<string> AddPrincipalAsync(string stem, string? permission)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var externalId = stem + "-" + Guid.NewGuid().ToString("N")[..8];
        var userId = "user-" + externalId;
        db.AddUsersWithCurrentUsernames(new UserEntity { Id = userId, Username = externalId, DisplayName = externalId, IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity { Id = "link-" + externalId, UserId = userId, Source = (int)IdentitySource.Local, ExternalId = externalId });
        if (permission is not null)
        {
            var roleId = "role-" + externalId;
            db.Roles.Add(new RoleEntity
            {
                Id = roleId,
                Name = "Entra probe test " + externalId,
                Description = "test",
                PermissionsJson = $$$"""[{"action":"{{{permission}}}","resource":{"resourceType":"InstanceConfiguration","scopeKey":"instance"}}]""",
                IsSystemRole = false,
            });
            db.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleId = roleId });
        }

        await db.SaveChangesAsync();
        return externalId;
    }

    /// <summary>A registration inserted as test fixtures insert them: configuration only, with no diagnostics row.</summary>
    private async Task<string> AddRegistrationAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var id = "s24reg" + Guid.NewGuid().ToString("N")[..12];
        var now = DateTimeOffset.UtcNow;
        db.EntraInstanceRegistrations.Add(new EntraInstanceRegistrationEntity
        {
            Id = id,
            DisplayName = "Probe authorization test " + id,
            Authority = Audit.EntraRegistrationSqlHarness.Authority,
            ClientId = Audit.EntraRegistrationSqlHarness.ClientId,
            AzureTenantId = Audit.EntraRegistrationSqlHarness.TenantId,
            UsesDeploymentSecret = true,
            Enabled = true,
            IsActive = false,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<(byte[] RowVersion, DateTimeOffset UpdatedUtc, string DisplayName)> RegistrationRowAsync(string id)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var row = await db.EntraInstanceRegistrations.AsNoTracking().SingleAsync(r => r.Id == id);
        return (row.RowVersion, row.UpdatedUtc, row.DisplayName);
    }

    private async Task<EntraRegistrationDiagnosticsEntity?> DiagnosticsAsync(string id)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        return await db.EntraRegistrationDiagnostics.AsNoTracking().SingleOrDefaultAsync(d => d.RegistrationId == id);
    }
}
