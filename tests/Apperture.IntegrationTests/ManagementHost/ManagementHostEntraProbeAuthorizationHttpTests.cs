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
/// SEC-2.4 (acceptance A4.4) and IAM-10.2a (A10.2.15) on the real Management.Host pipeline: who may run an Entra probe, and a
/// refused or unauthenticated request neither reaches the probe nor updates the registration's telemetry.
/// <list type="bullet">
/// <item><c>auth.directory.search</c> is the one permission the directory searches (users and groups) accept. The
/// <c>auth.configuration.read</c> and <c>auth.entra.manage</c> permissions that used to reach them no longer do, and there is no
/// period in which either is accepted.</item>
/// <item><c>auth.configuration.read</c> is still refused <c>validate-openid</c>; <c>auth.entra.manage</c> keeps it.</item>
/// <item>A principal with none of them gets 403 on all three, an unauthenticated caller 401, and no probe runs.</item>
/// <item>A search by an authorized searcher records its outcome on the diagnostics row and leaves the registration row, including
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
    private const string SearchPermission = "auth.directory.search";

    [Fact]
    public async Task A_searcher_may_search_the_directory_and_the_search_updates_telemetry_only()
    {
        RequireSql();
        var reader = await AddPrincipalAsync("sec24-searcher", SearchPermission);
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
        // (Read through a reader added now, after the host start that ran the data step: the searcher cannot read the configuration.)
        var configReader = await AddPrincipalAsync("sec24-detail-reader", ReadPermission);
        using var detail = _fixture.CreateAuthenticatedClient(configReader);
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
    public async Task A_manager_keeps_the_openid_validation_and_is_no_longer_accepted_for_the_searches()
    {
        RequireSql();
        var registrationId = await AddRegistrationAsync();
        var validator = new Audit.EntraRegistrationSqlHarness.ScriptedValidator();
        var discovery = new Audit.EntraRegistrationSqlHarness.ScriptedDiscovery();
        // Starting a host runs the authorization data step, which would give a role that already exists the search permission
        // (as it should). The principal is added after the host has started, as an operator would add a role to a running instance.
        using var factory = WithProbes(discovery, validator);
        _ = factory.Server; // the host (and its data step) starts here, not at the first request
        var manager = await AddPrincipalAsync("sec24-manager", ManagePermission);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, manager);
        (await client.PostAsync($"/api/config/auth/entra-tenants/{registrationId}/validate-openid", content: null)).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var kind in new[] { "users", "groups" })
        {
            (await client.PostAsJsonAsync($"/api/config/auth/entra-tenants/{registrationId}/directory/{kind}", new { search = "dana", top = 5 }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"auth.entra.manage alone no longer reaches the {kind} search");
        }

        validator.Calls.Should().Be(1);
        discovery.Calls.Should().Be(0, "a refused search never reaches Microsoft Graph");
        var diagnostics = (await DiagnosticsAsync(registrationId))!;
        diagnostics.OpenIdConfigurationStatus.Should().Be((int)OpenIdConfigurationStatus.Valid);
        diagnostics.DirectoryDiscoveryStatus.Should().NotBe((int)DirectoryDiscoveryStatus.Success, "no search ran");
        diagnostics.LastDirectoryDiscoveryUtc.Should().BeNull("no search ran");
    }

    [Fact]
    public async Task A_reader_of_the_authentication_configuration_is_no_longer_accepted_for_the_searches()
    {
        RequireSql();
        var registrationId = await AddRegistrationAsync();
        var discovery = new Audit.EntraRegistrationSqlHarness.ScriptedDiscovery();
        // The role is added after the host has started: starting a host runs the data step, which gives existing readers the search.
        using var factory = WithProbes(discovery, new Audit.EntraRegistrationSqlHarness.ScriptedValidator());
        _ = factory.Server; // the host (and its data step) starts here, not at the first request
        var reader = await AddPrincipalAsync("sec24-reader-s", ReadPermission);

        foreach (var kind in new[] { "users", "groups" })
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, reader);
            (await client.PostAsJsonAsync($"/api/config/auth/entra-tenants/{registrationId}/directory/{kind}", new { search = "dana", top = 5 }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, kind);
        }

        discovery.Calls.Should().Be(0);
        (await DiagnosticsAsync(registrationId)).Should().BeNull("a refused request records nothing");
    }

    [Fact]
    public async Task A_searcher_is_refused_the_openid_validation_and_the_search_permission_grants_nothing_else()
    {
        RequireSql();
        var searcher = await AddPrincipalAsync("sec24-searcher-v", SearchPermission);
        var registrationId = await AddRegistrationAsync();
        var validator = new Audit.EntraRegistrationSqlHarness.ScriptedValidator();
        using var factory = WithProbes(new Audit.EntraRegistrationSqlHarness.ScriptedDiscovery(), validator);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, searcher);
        (await client.PostAsync($"/api/config/auth/entra-tenants/{registrationId}/validate-openid", content: null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/config/auth/entra-tenants")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "searching is not reading the configuration");

        validator.Calls.Should().Be(0);
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
