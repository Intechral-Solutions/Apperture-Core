using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Apperture.IntegrationTests.Audit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.ManagementHost;

/// <summary>
/// IAM-10.2a at the real Management Host boundary (A10.2.7 to A10.2.9, A10.2.13): real principals sign in to the host, and the
/// statuses, stable codes and bodies of the typed refusals are read from the wire, with the audit rows each decision wrote read back
/// from the database. A business refusal is never a 404; a genuine not-found is. Runs on a disposable catalog with locking READ
/// COMMITTED and on one with READ_COMMITTED_SNAPSHOT. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RoleAuthorityHostHttpTests : IClassFixture<RoleAuthorityHostHttpTests.Hosts>
{
    private const string AssignmentRejected = "governance.role.assignment_rejected";
    private const string MutationRejected = "governance.role.mutation_rejected";
    private const string Assigned = "governance.role.assignment.created";
    private const string Removed = "governance.role.assignment.removed";
    private const string BlockedLastAdmin = "auth_admin.blocked.last_platform_admin_removal";

    private readonly Hosts _hosts;
    private readonly ITestOutputHelper _output;

    public RoleAuthorityHostHttpTests(Hosts hosts, ITestOutputHelper output)
    {
        _hosts = hosts;
        _output = output;
    }

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    /// <summary>One host per isolation mode, each on its own throwaway catalog.</summary>
    public sealed class Hosts : IAsyncLifetime
    {
        public AuditSqlScratch Locking { get; private set; } = null!;
        public AuditSqlScratch Rcsi { get; private set; } = null!;
        public ManagementHostWebApplicationFactory LockingHost { get; private set; } = null!;
        public ManagementHostWebApplicationFactory RcsiHost { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Locking = await AuditIsolation.CreateAsync("Apperture_Iam10Host", rcsi: false);
            Rcsi = await AuditIsolation.CreateAsync("Apperture_Iam10Host", rcsi: true);
            LockingHost = await StartAsync(Locking);
            RcsiHost = await StartAsync(Rcsi);
        }

        private static async Task<ManagementHostWebApplicationFactory> StartAsync(AuditSqlScratch scratch)
        {
            var factory = new ManagementHostWebApplicationFactory(scratch.ConnectionString);
            using var client = factory.CreateClient();
            _ = await client.GetAsync("/signin"); // starts the host, which migrates, registers the catalog and repairs the administrator role
            return factory;
        }

        public async Task DisposeAsync()
        {
            await LockingHost.DisposeAsync();
            await RcsiHost.DisposeAsync();
            await Locking.DisposeAsync();
            await Rcsi.DisposeAsync();
        }
    }

    private (RoleAuthorityWorld World, ManagementHostWebApplicationFactory Host) For(bool rcsi) =>
        rcsi ? (new RoleAuthorityWorld(_hosts.Rcsi), _hosts.RcsiHost) : (new RoleAuthorityWorld(_hosts.Locking), _hosts.LockingHost);

    private static HttpClient As(ManagementHostWebApplicationFactory host, RoleAuthorityWorld.Person person)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(ManagementTestAuthHandler.HeaderName, person.Username);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<(RoleAuthorityWorld.Person Escalator, string Subset, string Exceeding)> EscalatorAsync(RoleAuthorityWorld world)
    {
        var escalator = await world.NewPersonAsync("escalator", await world.NewRoleAsync("escalator", RoleAssign, PermissionAssign, RoleManage, P));
        return (escalator, await world.NewRoleAsync("subset", P), await world.NewRoleAsync("exceeding", P, Q));
    }

    // ---- A10.2.7: promotion to Platform Administrator ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Assigning_the_administrator_role_as_a_non_administrator_is_403_platform_administrator_restricted_with_one_refusal_row(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var (escalator, _, _) = await EscalatorAsync(world);
        var colleague = await world.NewPersonAsync("colleague");

        foreach (var target in new[] { escalator.Id, colleague.Id })
        {
            using var client = As(host, escalator);
            var response = await client.PostAsJsonAsync($"api/users/{target}/roles", new { roleId = AdminRoleId });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await BodyAsync(response);
            body.GetProperty("code").GetString().Should().Be("platform_administrator_restricted");
            body.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
            (await world.AssignmentExistsAsync(target, AdminRoleId)).Should().BeFalse();
            (await world.AuditRowsAsync(AssignmentRejected, target)).Should().Be(1);
        }

        _output.WriteLine(world.Scratch.ExecutionMarker);
    }

    // ---- A10.2.8: Rule 1 ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Replacing_permissions_with_one_the_actor_lacks_is_403_grant_exceeds_authority_listing_it_and_leaves_the_role_unchanged(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var (escalator, _, _) = await EscalatorAsync(world);
        var target = await world.NewRoleAsync("target", P);

        using var client = As(host, escalator);
        var response = await client.PutAsJsonAsync($"api/roles/{target}/permissions", new
        {
            permissions = new[]
            {
                new { action = "connection.read", resourceType = "Connection", scopeKey = "instance" },
                new { action = "integration.read", resourceType = "Integration", scopeKey = "instance" },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await BodyAsync(response);
        body.GetProperty("code").GetString().Should().Be("grant_exceeds_authority");
        body.GetProperty("error").GetString().Should().Be("grant_exceeds_authority", "the roles routes keep their error property");
        body.GetProperty("missingCount").GetInt32().Should().Be(1);
        body.GetProperty("missing").EnumerateArray().Select(e => e.GetString()).Should().Equal("integration.read@Integration:instance");
        (await world.RoleGrantsAsync(target)).Should().BeEquivalentTo([P]);
        (await world.AuditRowsAsync(MutationRejected, target)).Should().Be(1);

        var allowed = await client.PutAsJsonAsync($"api/roles/{target}/permissions", new
        {
            permissions = new[] { new { action = "connection.read", resourceType = "Connection", scopeKey = "instance" } },
        });
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Creating_a_role_with_grants_the_actor_lacks_is_403_and_one_within_their_authority_is_201(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var (escalator, _, _) = await EscalatorAsync(world);
        var rolesBefore = await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Roles;");
        var name = "Host role " + Guid.NewGuid().ToString("N")[..6];

        using var client = As(host, escalator);
        var refused = await client.PostAsJsonAsync("api/roles", new
        {
            name,
            permissions = new[]
            {
                new { action = "connection.read", resourceType = "Connection", scopeKey = "instance" },
                new { action = "schedule.manage", resourceType = "Schedule", scopeKey = "instance" },
            },
        });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyAsync(refused)).GetProperty("code").GetString().Should().Be("grant_exceeds_authority");
        (await world.Scratch.ScalarAsync<int>("SELECT COUNT(*) FROM Roles;")).Should().Be(rolesBefore);

        var created = await client.PostAsJsonAsync("api/roles", new
        {
            name,
            permissions = new[] { new { action = "connection.read", resourceType = "Connection", scopeKey = "instance" } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- A10.2.9: Rule 2, the order of P-4, and refusals that are not 404 ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Assignment_and_removal_enforce_the_subset_rule_and_a_no_op_is_204_only_for_an_authorized_actor(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var (escalator, subset, exceeding) = await EscalatorAsync(world);
        var user = await world.NewPersonAsync("user");
        using var client = As(host, escalator);

        var tooMuch = await client.PostAsJsonAsync($"api/users/{user.Id}/roles", new { roleId = exceeding });
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var tooMuchBody = await BodyAsync(tooMuch);
        tooMuchBody.GetProperty("code").GetString().Should().Be("assignment_exceeds_authority");
        tooMuchBody.GetProperty("missing").EnumerateArray().Select(e => e.GetString()).Should().Equal("integration.read@Integration:instance");

        (await client.PostAsJsonAsync($"api/users/{user.Id}/roles", new { roleId = subset })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.AuditRowsAsync(Assigned, user.Id)).Should().Be(1);
        (await client.PostAsJsonAsync($"api/users/{user.Id}/roles", new { roleId = subset })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.AuditRowsAsync(Assigned, user.Id)).Should().Be(1, "an authorized no-op is a silent success");

        var unauthorizedNoOp = await client.DeleteAsync($"api/users/{user.Id}/roles/{exceeding}");
        unauthorizedNoOp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the user does not hold it, but the actor may not manage it: not a 204");
        (await BodyAsync(unauthorizedNoOp)).GetProperty("code").GetString().Should().Be("removal_exceeds_authority");

        (await client.DeleteAsync($"api/users/{user.Id}/roles/{subset}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.AuditRowsAsync(Removed, user.Id)).Should().Be(1);
        (await client.DeleteAsync($"api/users/{user.Id}/roles/{subset}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await world.AuditRowsAsync(Removed, user.Id)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_role_whose_stored_permissions_cannot_be_read_is_409_on_assignment(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var (escalator, _, _) = await EscalatorAsync(world);
        var user = await world.NewPersonAsync("user");
        var broken = await world.NewRawRoleAsync("broken", "{not json");

        using var client = As(host, escalator);
        var response = await client.PostAsJsonAsync($"api/users/{user.Id}/roles", new { roleId = broken });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).GetProperty("code").GetString().Should().Be("role_permissions_unreadable");
        (await world.AssignmentExistsAsync(user.Id, broken)).Should().BeFalse();
    }

    // ---- P-37: not found and validation are not refusals, and refusals are not 404 ----

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_missing_role_or_user_is_404_with_a_code_and_a_malformed_request_is_400(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var (escalator, subset, _) = await EscalatorAsync(world);
        var user = await world.NewPersonAsync("user");
        using var client = As(host, escalator);

        var noRole = await client.PostAsJsonAsync($"api/users/{user.Id}/roles", new { roleId = "role-missing-" + Guid.NewGuid().ToString("N") });
        noRole.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(noRole)).GetProperty("code").GetString().Should().Be("role_not_found");

        var noUser = await client.PostAsJsonAsync($"api/users/ghost-{Guid.NewGuid():N}/roles", new { roleId = subset });
        noUser.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(noUser)).GetProperty("code").GetString().Should().Be("user_not_found");

        var noRoleToDelete = await client.DeleteAsync("api/roles/role-missing-" + Guid.NewGuid().ToString("N"));
        noRoleToDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var noName = await client.PostAsJsonAsync("api/roles", new { name = "  " });
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noNameBody = await BodyAsync(noName);
        noNameBody.GetProperty("code").GetString().Should().Be("role_name_required");
        noNameBody.GetProperty("error").GetString().Should().Be("role_name_required");

        var unknownPermission = await client.PutAsJsonAsync($"api/roles/{subset}/permissions", new
        {
            permissions = new[] { new { action = "not.in", resourceType = "Catalog", scopeKey = "instance" } },
        });
        unknownPermission.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(unknownPermission)).GetProperty("code").GetString().Should().Be("invalid_permission");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task An_actor_without_the_permission_is_403_with_no_row_as_before(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var nobody = await world.NewPersonAsync("nobody");
        var user = await world.NewPersonAsync("user");
        var subset = await world.NewRoleAsync("subset", P);

        using var client = As(host, nobody);
        var response = await client.PostAsJsonAsync($"api/users/{user.Id}/roles", new { roleId = subset });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await world.AuditRowsAsync(AssignmentRejected, user.Id)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Deleting_an_assigned_role_stays_409_assignments_remain_with_the_count(bool rcsi)
    {
        var (world, host) = For(rcsi);
        var deleter = await world.NewPersonAsync("deleter", await world.NewRoleAsync("deleter", RoleManage));
        var role = await world.NewRoleAsync("assigned", P);
        await world.NewPersonAsync("holder", role);

        using var client = As(host, deleter);
        var response = await client.DeleteAsync($"api/roles/{role}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(response);
        body.GetProperty("code").GetString().Should().Be("assignments_remain");
        body.GetProperty("assignmentCount").GetInt32().Should().Be(1);
    }

    // ---- The last administrator: a typed 409 instead of the old 404 ----

    [Fact]
    public async Task Removing_the_last_administrator_role_assignment_is_409_last_viable_administrator_and_not_404()
    {
        // Its own catalog, because the answer depends on how many administrators there are.
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_Iam10Last", rcsi: false);
        var world = new RoleAuthorityWorld(scratch);
        var sole = await world.NewPersonAsync("sole-admin", AdminRoleId);
        await using var host = new ManagementHostWebApplicationFactory(scratch.ConnectionString);
        using (var warmUp = host.CreateClient())
            _ = await warmUp.GetAsync("/signin");

        using var client = As(host, sole);
        var response = await client.DeleteAsync($"api/users/{sole.Id}/roles/{AdminRoleId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response)).GetProperty("code").GetString().Should().Be("last_viable_administrator");
        (await world.AssignmentExistsAsync(sole.Id, AdminRoleId)).Should().BeTrue();
        (await world.AuditRowsAsync(BlockedLastAdmin, sole.Id)).Should().Be(1);
    }
}
