using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// IAM-10.2a slice 3 through the REAL Management Host (real authorization, real roles in SQL Server): the removal of the
/// <c>role.manage</c> implication, the move of the three governance-read consumers to <c>user.read</c> on User, the
/// <c>operational_team.manage</c> / <c>auth.directory.search</c> / <c>connection.verify</c> switches, and the capability flags
/// that make them visible (A10.2.2, A10.2.15, A10.2.17, and the deny-only upgrade window P-39). A principal is a role holding the
/// listed grants; the roles are inserted as a database from before the data step would hold them. Fails, never skips, without SQL.
/// </summary>
[Collection(ManagementHostGovernanceCollection.Name)]
public sealed class AuthorizationRemovalHostHttpTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ManagementHostGovernanceFixture _fixture;

    public AuthorizationRemovalHostHttpTests(ManagementHostGovernanceFixture fixture) => _fixture = fixture;

    private void RequireSql() =>
        _fixture.SqlAvailable.Should().BeTrue("the fixture fails hard: " + SqlServerTestConfiguration.DescribeActiveConfiguration());

    private static string Grant(string action, string type, string scope = "instance") =>
        "{\"action\":\"" + action + "\",\"resource\":{\"resourceType\":\"" + type + "\",\"scopeKey\":\"" + scope + "\"}}";

    private static string Roles(params string[] grants) => "[" + string.Join(",", grants) + "]";

    /// <summary>A local principal holding one role with exactly the given stored permissions JSON. Returns the external id and the role id.</summary>
    private async Task<(string External, string RoleId)> AddPrincipalAsync(string stem, string permissionsJson)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var external = "iam10s3-" + stem + "-" + Guid.NewGuid().ToString("N")[..8];
        var userId = "user-" + external;
        var roleId = "role-" + external;
        db.Roles.Add(new RoleEntity { Id = roleId, Name = "Authorization removal test " + external, Description = "test", PermissionsJson = permissionsJson, IsSystemRole = false });
        db.AddUsersWithCurrentUsernames(new UserEntity { Id = userId, Username = external, DisplayName = external, IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity { Id = "link-" + external, UserId = userId, Source = (int)IdentitySource.Local, ExternalId = external });
        db.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
        return (external, roleId);
    }

    /// <summary>A local user holding the seeded Platform Administrator role, whose stored grants are whatever the host's startup repair left.</summary>
    private async Task<string> AddAdministratorAsync()
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var external = "iam10s3-admin-" + Guid.NewGuid().ToString("N")[..8];
        var userId = "user-" + external;
        db.AddUsersWithCurrentUsernames(new UserEntity { Id = userId, Username = external, DisplayName = external, IsBreakGlassEligible = false });
        db.IdentityLinks.Add(new IdentityLinkEntity { Id = "link-" + external, UserId = userId, Source = (int)IdentitySource.Local, ExternalId = external });
        db.UserRoles.Add(new UserRoleEntity { UserId = userId, RoleId = Apperture.Core.Application.BootstrapConstants.PlatformAdminRoleId });
        await db.SaveChangesAsync();
        return external;
    }

    /// <summary>Applies the data step's plan for one role directly, so only the roles of this test change.</summary>
    private async Task ApplyThePlanAsync(string roleId)
    {
        await using var scope = _fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppertureDbContext>();
        var row = await db.Roles.SingleAsync(r => r.Id == roleId);
        var plan = RoleAuthorizationDataStep.PlanChanges([(row.Id, row.IsSystemRole, row.PermissionsJson)]);
        plan.Changes.Should().ContainSingle();
        row.PermissionsJson = plan.Changes.Single().PermissionsJson;
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> CapabilitiesAsync(string external)
    {
        using var client = _fixture.CreateAuthenticatedClient(external);
        var response = await client.GetAsync("/api/control/capabilities");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private async Task<HttpStatusCode> GetStatusAsync(string external, string url)
    {
        using var client = _fixture.CreateAuthenticatedClient(external);
        return (await client.GetAsync(url)).StatusCode;
    }

    private static readonly string[] AdministrativeFlags =
    [
        "canViewGovernance", "canAssignPermissions", "canAssignRoles", "canManageAuthUiPolicy", "canViewAuthConfiguration",
        "canManageEntraTenants", "canManageUsers", "canManageCredentials",
    ];

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.2 and P-39: a role.manage-only role, before and after the preserving step
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_custom_role_holding_only_role_manage_is_refused_everything_it_used_to_imply_until_the_step_has_run_and_then_gets_exactly_that_back()
    {
        RequireSql();
        var (external, roleId) = await AddPrincipalAsync("rolemanage", Roles(Grant("role.manage", "Role")));

        // Upgrade window: new code, pre-step data. Deny-only.
        var window = await CapabilitiesAsync(external);
        window.GetProperty("canManageRoles").GetBoolean().Should().BeTrue("role.manage still allows role.manage");
        foreach (var flag in AdministrativeFlags)
            window.GetProperty(flag).GetBoolean().Should().BeFalse($"{flag} used to be implied by role.manage and is not any more");
        window.GetProperty("canSearchDirectory").GetBoolean().Should().BeFalse();
        window.GetProperty("canVerifyConnections").GetBoolean().Should().BeFalse();
        (await GetStatusAsync(external, "/api/governance/descriptors")).Should().Be(HttpStatusCode.Forbidden);
        (await GetStatusAsync(external, "/api/config/auth/entra-tenants")).Should().Be(HttpStatusCode.Forbidden);

        // After the step: the same principal can do what it could do before (the eight), through ordinary explicit grants.
        await ApplyThePlanAsync(roleId);

        var migrated = await CapabilitiesAsync(external);
        foreach (var flag in AdministrativeFlags)
            migrated.GetProperty(flag).GetBoolean().Should().BeTrue($"{flag} is preserved by an explicit grant");
        migrated.GetProperty("canSearchDirectory").GetBoolean().Should().BeTrue("the role could search through the implied auth.configuration.read");
        migrated.GetProperty("canVerifyConnections").GetBoolean().Should().BeFalse("role.manage never gave connection access");
        migrated.GetProperty("canManageSchedules").GetBoolean().Should().BeFalse();
        migrated.GetProperty("canViewConnections").GetBoolean().Should().BeFalse();
        (await GetStatusAsync(external, "/api/governance/descriptors")).Should().Be(HttpStatusCode.OK);
        (await GetStatusAsync(external, "/api/config/auth/entra-tenants")).Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P-18 / A10.2.17: governance read is user.read on User
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_role_holding_only_user_read_on_User_sees_governance_and_can_do_nothing_else()
    {
        RequireSql();
        var (external, _) = await AddPrincipalAsync("userread", Roles(Grant("user.read", "User")));

        var capabilities = await CapabilitiesAsync(external);

        capabilities.GetProperty("canViewGovernance").GetBoolean().Should().BeTrue();
        capabilities.GetProperty("canManageUsers").GetBoolean().Should().BeFalse();
        capabilities.GetProperty("canAssignRoles").GetBoolean().Should().BeFalse();
        capabilities.GetProperty("canAssignPermissions").GetBoolean().Should().BeFalse();
        (await GetStatusAsync(external, "/api/governance/descriptors")).Should().Be(HttpStatusCode.OK);

        using var client = _fixture.CreateAuthenticatedClient(external);
        var rename = await client.PutAsJsonAsync($"/api/users/user-{external}/username", new { username = external + "-renamed" });
        rename.StatusCode.Should().Be(HttpStatusCode.Forbidden, "user.read does not allow renaming");
    }

    [Fact]
    public async Task A_role_holding_only_the_obsolete_user_read_on_Role_no_longer_sees_governance()
    {
        RequireSql();
        var (external, _) = await AddPrincipalAsync("stopgap", Roles(Grant("user.read", "Role")));

        (await CapabilitiesAsync(external)).GetProperty("canViewGovernance").GetBoolean().Should().BeFalse();
        (await GetStatusAsync(external, "/api/governance/descriptors")).Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P-17: team management
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Team_routes_need_operational_team_manage_and_schedule_manage_alone_is_refused()
    {
        RequireSql();
        var (scheduler, schedulerRole) = await AddPrincipalAsync("scheduler", Roles(Grant("schedule.manage", "Schedule")));
        var (teamManager, _) = await AddPrincipalAsync("teams", Roles(Grant("operational_team.manage", "OperationalTeam")));

        (await GetStatusAsync(scheduler, "/api/operational-teams")).Should().Be(HttpStatusCode.Forbidden, "pre-step data: nothing is accepted in place of the new permission");
        (await GetStatusAsync(teamManager, "/api/operational-teams")).Should().Be(HttpStatusCode.OK);
        (await GetStatusAsync(teamManager, "/api/schedules")).Should().Be(HttpStatusCode.Forbidden, "the team permission does not reach schedules");

        await ApplyThePlanAsync(schedulerRole);

        (await GetStatusAsync(scheduler, "/api/operational-teams")).Should().Be(HttpStatusCode.OK, "the step granted the schedule.manage holder the new permission");
        (await GetStatusAsync(scheduler, "/api/schedules")).Should().NotBe(HttpStatusCode.Forbidden, "schedule.manage keeps what it always allowed");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P-19: directory search and test-connect
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Directory_searches_follow_the_search_permission_and_the_flag_mirrors_the_routes()
    {
        RequireSql();
        var (reader, readerRole) = await AddPrincipalAsync("authread", Roles(Grant("auth.configuration.read", "InstanceConfiguration")));
        var (searcher, _) = await AddPrincipalAsync("searcher", Roles(Grant("auth.directory.search", "InstanceConfiguration")));
        var path = "/api/config/auth/entra-tenants/does-not-exist/directory/users";

        async Task<HttpStatusCode> SearchAsync(string external)
        {
            using var client = _fixture.CreateAuthenticatedClient(external);
            return (await client.PostAsJsonAsync(path, new { search = "dana", top = 5 })).StatusCode;
        }

        (await SearchAsync(reader)).Should().Be(HttpStatusCode.Forbidden);
        (await CapabilitiesAsync(reader)).GetProperty("canSearchDirectory").GetBoolean().Should().BeFalse();
        (await SearchAsync(searcher)).Should().Be(HttpStatusCode.NotFound, "authorized, then no such registration");
        (await CapabilitiesAsync(searcher)).GetProperty("canSearchDirectory").GetBoolean().Should().BeTrue();

        await ApplyThePlanAsync(readerRole);

        (await SearchAsync(reader)).Should().Be(HttpStatusCode.NotFound, "the step granted the reader the search permission");
        (await CapabilitiesAsync(reader)).GetProperty("canSearchDirectory").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Test_connect_follows_connection_verify_and_the_flag_mirrors_the_route()
    {
        RequireSql();
        using var creator = _fixture.CreateAuthenticatedClient(ManagementGovernanceTestConstants.ExternalIntegrationAuthoringOperator);
        var create = await creator.PostAsJsonAsync("/api/connections", new
        {
            connectorDefinitionId = "Apperture.Platform.Sql",
            name = $"Iam10 verify {Guid.NewGuid():N}"[..24],
            config = new Dictionary<string, string> { ["Server"] = "localhost", ["Database"] = "db", ["UserId"] = "sa" },
            secretValues = new Dictionary<string, string> { ["Password"] = "TestPassword123!" },
            scopeKey = "instance",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var connectionId = (await create.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString();

        var (reader, readerRole) = await AddPrincipalAsync("connread", Roles(Grant("connection.read", "Connection")));
        var (verifier, _) = await AddPrincipalAsync("connverify", Roles(Grant("connection.read", "Connection"), Grant("connection.verify", "Connection")));

        async Task<HttpStatusCode> TestConnectAsync(string external)
        {
            using var client = _fixture.CreateAuthenticatedClient(external);
            return (await client.PostAsync($"/api/connections/{connectionId}/test-connect", null)).StatusCode;
        }

        (await TestConnectAsync(reader)).Should().Be(HttpStatusCode.Forbidden, "connection.read alone no longer reaches test-connect");
        (await CapabilitiesAsync(reader)).GetProperty("canVerifyConnections").GetBoolean().Should().BeFalse();
        (await TestConnectAsync(verifier)).Should().NotBe(HttpStatusCode.Forbidden);
        (await CapabilitiesAsync(verifier)).GetProperty("canVerifyConnections").GetBoolean().Should().BeTrue();

        await ApplyThePlanAsync(readerRole);

        (await TestConnectAsync(reader)).Should().NotBe(HttpStatusCode.Forbidden, "the step granted the connection.read holder connection.verify");
        (await CapabilitiesAsync(reader)).GetProperty("canVerifyConnections").GetBoolean().Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The Platform Administrator floor is untouched
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_platform_administrator_keeps_its_floor_and_gets_the_switched_permissions_from_the_repair()
    {
        RequireSql();
        var admin = await AddAdministratorAsync();

        var capabilities = await CapabilitiesAsync(admin);
        foreach (var flag in AdministrativeFlags)
            capabilities.GetProperty(flag).GetBoolean().Should().BeTrue(flag);
        capabilities.GetProperty("canSearchDirectory").GetBoolean().Should().BeTrue("the repair lists give the administrator the search");
        capabilities.GetProperty("canVerifyConnections").GetBoolean().Should().BeTrue("the repair lists give the administrator test-connect");
        (await GetStatusAsync(admin, "/api/operational-teams")).Should().Be(HttpStatusCode.OK, "the repair lists give the administrator team management");
        (await GetStatusAsync(admin, "/api/governance/descriptors")).Should().Be(HttpStatusCode.OK);
    }
}
