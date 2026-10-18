using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Persistence.SqlServer;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;
using Report = Apperture.Persistence.SqlServer.RoleAuthorizationDataStep.Report;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// IAM-10.2a slice 3 against real SQL Server: the startup data step that preserves what the removed <c>role.manage</c> implication
/// gave and grants the replacement permissions (IAM-10 gate P-32, P-33, P-17, P-19; A10.2.6, A10.2.14, A10.2.15 and the migration
/// test matrix of section 7.3). Every case runs on a throwaway migrated catalog of its own, because the audit table is append-only
/// and the step looks at every custom role. Fails, never skips, without SQL Server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class RoleAuthorizationDataStepSqlServerTests
{
    private readonly ITestOutputHelper _output;

    public RoleAuthorizationDataStepSqlServerTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> BothModes() => new[] { new object[] { false }, new object[] { true } };

    private const string StepAction = "governance.authorization.grants_migrated";

    private static Task<AuditSqlScratch> NewCatalogAsync(bool rcsi) => AuditIsolation.CreateAsync("Apperture_Iam10s3_", rcsi);

    // ---------------------------------------------------------------------------------------------------------------
    // Seeding and reading
    // ---------------------------------------------------------------------------------------------------------------

    private static string G(string action, string? type = null, string scope = "instance") =>
        type is null
            ? "{\"action\":\"" + action + "\"}"
            : "{\"action\":\"" + action + "\",\"resource\":{\"resourceType\":\"" + type + "\",\"scopeKey\":\"" + scope + "\"}}";

    private static string Json(params string[] grants) => "[" + string.Join(",", grants) + "]";

    private static Task AddRoleAsync(AuditSqlScratch scratch, string id, string permissionsJson, bool system = false) =>
        scratch.ExecuteAsync(
            $"INSERT INTO Roles (Id, Name, Description, PermissionsJson, IsSystemRole) VALUES (N'{id}', N'{id} name', NULL, N'{permissionsJson.Replace("'", "''")}', {(system ? 1 : 0)});");

    private static async Task<string> StoredJsonAsync(AuditSqlScratch scratch, string id) =>
        await scratch.ScalarAsync<string>($"SELECT PermissionsJson FROM Roles WHERE Id = N'{id}';");

    private static async Task<List<string>> IdentitiesAsync(AuditSqlScratch scratch, string id) =>
        StoredRolePermissions.Read(await StoredJsonAsync(scratch, id)).Permissions.Select(PermissionSetChange.Identity).Order(StringComparer.Ordinal).ToList();

    private static async Task<List<Role>> LoadRolesAsync(AuditSqlScratch scratch, params string[] ids)
    {
        var roles = new List<Role>();
        foreach (var id in ids)
        {
            var reading = StoredRolePermissions.Read(await StoredJsonAsync(scratch, id));
            var system = await scratch.ScalarAsync<bool>($"SELECT IsSystemRole FROM Roles WHERE Id = N'{id}';");
            roles.Add(new Role(new RoleId(id), id, null, reading.Permissions, system));
        }

        return roles;
    }

    private static List<string> Expected(params string[] identities) => identities.Order(StringComparer.Ordinal).ToList();

    private const string PermissionAssignId = "permission.assign@Role:instance";
    private const string RoleAssignId = "role.assign@Role:instance";
    private const string AuthReadId = "auth.configuration.read@InstanceConfiguration:instance";
    private const string AuthUiId = "auth.ui_policy.manage@InstanceConfiguration:instance";
    private const string EntraManageId = "auth.entra.manage@InstanceConfiguration:instance";
    private const string UserReadId = "user.read@User:instance";
    private const string UserManageId = "user.manage@User:instance";
    private const string CredentialId = "credential.manage@User:instance";
    private const string RoleManageId = "role.manage@Role:instance";
    private const string DirectorySearchId = "auth.directory.search@InstanceConfiguration:instance";
    private const string VerifyId = "connection.verify@Connection:instance";
    private const string ConnectionReadId = "connection.read@Connection:instance";
    private const string TeamId = "operational_team.manage@OperationalTeam:instance";
    private const string ScheduleId = "schedule.manage@Schedule:instance";

    /// <summary>The eight the implication used to give as catalog permissions.</summary>
    private static readonly string[] Eight =
        [PermissionAssignId, RoleAssignId, AuthReadId, AuthUiId, EntraManageId, UserReadId, UserManageId, CredentialId];

    // ---------------------------------------------------------------------------------------------------------------
    // The migration matrix (section 7.3)
    // ---------------------------------------------------------------------------------------------------------------

    private static async Task SeedMatrixAsync(AuditSqlScratch s)
    {
        await AddRoleAsync(s, "r-rm-only", Json(G("role.manage", "Role")));
        await AddRoleAsync(s, "r-rm-partial", Json(G("role.manage", "Role"), G("user.read", "User"), G("credential.manage", "User")));
        await AddRoleAsync(s, "r-rm-module", Json(G("role.manage", "Role", "module:X")));
        await AddRoleAsync(s, "r-rm-other-type", Json(G("role.manage", "User")));
        await AddRoleAsync(s, "r-rm-no-resource", Json(G("role.manage")));
        await AddRoleAsync(s, "r-rm-upper", Json(G("ROLE.MANAGE", "role")));
        await AddRoleAsync(s, "r-rm-instance-capital", Json(G("role.manage", "Role", "Instance")));
        await AddRoleAsync(s, "r-no-rm", Json(G("integration.read", "Integration")));
        await AddRoleAsync(s, "r-unreadable", "{not json");
        await AddRoleAsync(s, "r-unreadable-element", "[null]");
        await AddRoleAsync(s, "r-broad-and-rm", Json(G("role.manage", "Role"), G("workflow.start")));
        await AddRoleAsync(s, "r-broad-only", Json(G("user.read")));
        await AddRoleAsync(s, "r-schedule", Json(G("schedule.manage", "Schedule")));
        await AddRoleAsync(s, "r-schedule-module", Json(G("schedule.manage", "Schedule", "module:X")));
        await AddRoleAsync(s, "r-schedule-other-type", Json(G("schedule.manage", "Integration")));
        await AddRoleAsync(s, "r-auth-read", Json(G("auth.configuration.read", "InstanceConfiguration")));
        await AddRoleAsync(s, "r-entra-manage", Json(G("auth.entra.manage", "InstanceConfiguration")));
        await AddRoleAsync(s, "r-auth-ui-only", Json(G("auth.ui_policy.manage", "InstanceConfiguration")));
        await AddRoleAsync(s, "r-conn-read", Json(G("connection.read", "Connection")));
        await AddRoleAsync(s, "r-conn-read-module", Json(G("connection.read", "Connection", "module:X")));
        await AddRoleAsync(s, "r-conn-browse", Json(G("connection.browse", "Connection")));
        await AddRoleAsync(s, "r-stopgap", Json(G("user.read", "Role"), G("connection.read", "Connection")));
        await AddRoleAsync(s, "r-stopgap-only", Json(G("user.read", "Role")));
        await AddRoleAsync(s, "r-stopgap-with-workflow", Json(G("user.read", "Role"), G("workflow.execute", "WorkflowInstance")));
        await AddRoleAsync(s, "r-relationship", Json(G("relationship.manage", "TrustRelationship")));
        await AddRoleAsync(s, "r-sys-rm", Json(G("role.manage", "Role")), system: true);
        await AddRoleAsync(s, "r-empty", "[]");
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_step_gives_each_role_exactly_the_grants_the_gate_names_and_nothing_else(bool rcsi)
    {
        await using var s = await NewCatalogAsync(rcsi);
        await SeedMatrixAsync(s);
        var adminBefore = await StoredJsonAsync(s, "role-platform-administrator");

        await using (var db = s.NewDb())
            await RoleAuthorizationDataStep.RunAsync(db);

        // role.manage on Role at instance scope: the eight, and the holders of auth reads get the directory search.
        (await IdentitiesAsync(s, "r-rm-only")).Should().Equal(Expected([RoleManageId, .. Eight, DirectorySearchId]),
            "the eight explicit grants, and the directory search because the role could search through the implied auth.configuration.read");
        (await IdentitiesAsync(s, "r-rm-partial")).Should().Equal(Expected([RoleManageId, .. Eight, DirectorySearchId]), "only the missing ones are added, none twice");
        (await IdentitiesAsync(s, "r-rm-upper")).Should().Equal(Expected(["ROLE.MANAGE@role:instance", .. Eight, DirectorySearchId]), "action and type compare ignoring case");

        // role.manage that did not imply anything stays as it is.
        (await IdentitiesAsync(s, "r-rm-module")).Should().Equal(Expected("role.manage@Role:module:X"));
        (await IdentitiesAsync(s, "r-rm-other-type")).Should().Equal(Expected("role.manage@User:instance"));
        (await IdentitiesAsync(s, "r-rm-no-resource")).Should().Equal(Expected("role.manage"), "a grant with no resource never implied anything");
        (await IdentitiesAsync(s, "r-rm-instance-capital")).Should().Equal(Expected("role.manage@Role:Instance"), "scope keys are case-sensitive, so this never implied anything");

        // Roles without role.manage.
        (await IdentitiesAsync(s, "r-no-rm")).Should().Equal(Expected("integration.read@Integration:instance"));
        (await IdentitiesAsync(s, "r-empty")).Should().BeEmpty();
        (await IdentitiesAsync(s, "r-relationship")).Should().Equal(Expected("relationship.manage@TrustRelationship:instance"), "the dormant permission is neither added nor removed by this step");

        // Unreadable data is listed and left exactly as stored.
        (await StoredJsonAsync(s, "r-unreadable")).Should().Be("{not json");
        (await StoredJsonAsync(s, "r-unreadable-element")).Should().Be("[null]");

        // Broad grants are not changed; the role still receives what its role.manage implied.
        (await IdentitiesAsync(s, "r-broad-and-rm")).Should().Equal(Expected([RoleManageId, "workflow.start", .. Eight, DirectorySearchId]));
        (await IdentitiesAsync(s, "r-broad-only")).Should().Equal(Expected("user.read"));

        // schedule.manage on Schedule at instance scope gains team management.
        (await IdentitiesAsync(s, "r-schedule")).Should().Equal(Expected(ScheduleId, TeamId));
        (await IdentitiesAsync(s, "r-schedule-module")).Should().Equal(Expected("schedule.manage@Schedule:module:X"));
        (await IdentitiesAsync(s, "r-schedule-other-type")).Should().Equal(Expected("schedule.manage@Integration:instance"));

        // auth.configuration.read or auth.entra.manage gains the directory search; auth.ui_policy.manage alone does not.
        (await IdentitiesAsync(s, "r-auth-read")).Should().Equal(Expected(AuthReadId, DirectorySearchId));
        (await IdentitiesAsync(s, "r-entra-manage")).Should().Equal(Expected(EntraManageId, DirectorySearchId));
        (await IdentitiesAsync(s, "r-auth-ui-only")).Should().Equal(Expected(AuthUiId));

        // connection.read gains connection.verify; browse alone and a module-scoped read do not.
        (await IdentitiesAsync(s, "r-conn-read")).Should().Equal(Expected(ConnectionReadId, VerifyId));
        (await IdentitiesAsync(s, "r-conn-read-module")).Should().Equal(Expected("connection.read@Connection:module:X"));
        (await IdentitiesAsync(s, "r-conn-browse")).Should().Equal(Expected("connection.browse@Connection:instance"));

        // The obsolete read on Role is removed wherever it is found and nothing else is.
        (await IdentitiesAsync(s, "r-stopgap")).Should().Equal(Expected(ConnectionReadId, VerifyId));
        (await StoredJsonAsync(s, "r-stopgap-only")).Should().Be("[]");
        (await IdentitiesAsync(s, "r-stopgap-with-workflow")).Should().Equal(Expected("workflow.execute@WorkflowInstance:instance"), "the instance-level workflow.execute grant is never disturbed");

        // System roles are never touched by the step.
        (await IdentitiesAsync(s, "r-sys-rm")).Should().Equal(Expected(RoleManageId));
        (await StoredJsonAsync(s, "role-platform-administrator")).Should().Be(adminBefore);

        // The implication's two non-grants were never created.
        foreach (var id in new[] { "r-rm-only", "r-rm-partial", "r-rm-upper", "r-broad-and-rm" })
        {
            (await IdentitiesAsync(s, id)).Should().NotContain("user.read@Role:instance").And.NotContain("role.assign@User:instance");
        }

        _output.WriteLine(s.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task One_summary_event_lists_the_changed_roles_the_added_identities_and_the_roles_left_alone(bool rcsi)
    {
        await using var s = await NewCatalogAsync(rcsi);
        await SeedMatrixAsync(s);

        Report report;
        await using (var db = s.NewDb())
            report = await RoleAuthorizationDataStep.RunAsync(db);

        var rows = await s.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{StepAction}';");
        rows.Should().ContainSingle("one summary event for the whole run");
        var row = rows[0];
        row["Actor"].Should().Be("system:data-step");
        row["ActorKind"].Should().Be("system");
        row["Category"].Should().Be("governance");
        row["Severity"].Should().Be("elevated");
        row["Basis"].Should().Be("change");
        row["OutcomeCode"].Should().Be("succeeded");
        row["TargetType"].Should().BeNull();
        row["Producer"].Should().Be("recorder");
        row["DetailsFormat"].Should().Be("json/1");

        var details = JsonDocument.Parse((string)row["Details"]!).RootElement;
        details.GetProperty("rolesChanged").GetInt32().Should().Be(report.RolesChanged);
        details.GetProperty("grantsAdded").GetInt32().Should().Be(report.GrantsAdded);
        details.GetProperty("grantsRemoved").GetInt32().Should().Be(report.GrantsRemoved);
        details.GetProperty("unreadableRoleCount").GetInt32().Should().Be(2);
        details.GetProperty("broadGrantRoleCount").GetInt32().Should().Be(3, "the roles r-rm-no-resource, r-broad-and-rm and r-broad-only each store a grant with no resource");

        var changed = details.GetProperty("changedRoles").EnumerateArray().Select(e => e.GetString()!).ToList();
        changed.Should().BeEquivalentTo(
            ["r-rm-only", "r-rm-partial", "r-rm-upper", "r-broad-and-rm", "r-schedule", "r-auth-read", "r-entra-manage", "r-conn-read", "r-stopgap", "r-stopgap-only", "r-stopgap-with-workflow"]);
        changed.Should().NotContain("r-sys-rm").And.NotContain("r-unreadable");
        details.GetProperty("unreadableRoles").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["r-unreadable", "r-unreadable-element"]);

        var changes = details.GetProperty("changes").EnumerateArray().Select(e => e.GetString()!).ToList();
        changes.Should().Contain($"r-rm-only|+{PermissionAssignId}");
        changes.Should().Contain($"r-rm-only|+{DirectorySearchId}");
        changes.Should().Contain($"r-schedule|+{TeamId}");
        changes.Should().Contain($"r-stopgap-only|-user.read@Role:instance");
        changes.Should().NotContain(c => c.StartsWith("r-rm-partial|+" + UserReadId, StringComparison.Ordinal), "a grant the role already held is not reported as added");

        // No role name, no stored JSON text and no user data is recorded.
        var text = (string)row["Details"]!;
        text.Should().NotContain("r-rm-only name").And.NotContain("\"action\"").And.NotContain("resourceType");
        _output.WriteLine(s.ExecutionMarker);
    }

    [Fact]
    public async Task The_report_names_the_counts_it_committed()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        await AddRoleAsync(s, "r-a", Json(G("role.manage", "Role")));
        await AddRoleAsync(s, "r-b", Json(G("schedule.manage", "Schedule"), G("user.read", "Role")));

        Report report;
        await using (var db = s.NewDb())
            report = await RoleAuthorizationDataStep.RunAsync(db);

        report.Committed.Should().BeTrue();
        report.RolesChanged.Should().Be(2);
        report.GrantsAdded.Should().Be(8 + 1 + 1, "r-a gets the eight and the directory search, r-b gets team management");
        report.GrantsRemoved.Should().Be(1);
        _output.WriteLine(s.ExecutionMarker);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // A10.2.6: effective access is preserved
    // ---------------------------------------------------------------------------------------------------------------

    private static readonly string[] PreservationRoles =
        ["r-rm-only", "r-rm-partial", "r-rm-module", "r-rm-other-type", "r-rm-no-resource", "r-rm-upper", "r-rm-instance-capital", "r-no-rm", "r-unreadable",
         "r-unreadable-element", "r-broad-and-rm", "r-broad-only", "r-schedule", "r-auth-read", "r-conn-read", "r-empty"];

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task Every_production_check_of_the_ten_combinations_gives_the_same_answer_before_and_after(bool rcsi)
    {
        await using var s = await NewCatalogAsync(rcsi);
        await SeedMatrixAsync(s);
        var before = await LoadRolesAsync(s, PreservationRoles);

        await using (var db = s.NewDb())
            await RoleAuthorizationDataStep.RunAsync(db);
        var after = await LoadRolesAsync(s, PreservationRoles);

        var scope = new ScopeContainmentEvaluator();
        var differences = new List<string>();
        var allowedBefore = 0;
        for (var i = 0; i < before.Count; i++)
        {
            var oldRoles = new[] { before[i] };
            var newSet = EffectivePermissionSet.FromRoles([after[i]], scope);
            foreach (var (action, type) in PreStepAuthorizationReference.TenCombinations)
            {
                // Row 1 (user.read on Role) is not preserved as a grant: its three consumers now check row 7 (user.read on User),
                // and row 9 (role.assign on User) has no consumer. Each is compared through what replaced it.
                var (newAction, newType) = (action, type) switch
                {
                    ("user.read", "Role") => ("user.read", "User"),
                    ("role.assign", "User") => ("", ""),
                    _ => (action, type),
                };
                if (newAction.Length == 0)
                    continue;

                var wasAllowed = PreStepAuthorizationReference.Allows(oldRoles, action, type);
                var isAllowed = newSet.Allows(new PermissionDescriptor(newAction, new ResourceDescriptor(newType, "instance")), "instance");
                if (wasAllowed) allowedBefore++;
                // A role that held user.read on Role explicitly keeps nothing for it: that is the one intended loss, not tested here.
                if (wasAllowed != isAllowed)
                    differences.Add($"{before[i].Id.Value}: {action}@{type} was {wasAllowed}, {newAction}@{newType} is {isAllowed}");
            }
        }

        differences.Should().BeEmpty("the step must preserve what every role could do before");
        allowedBefore.Should().BeGreaterThan(20, "the roles must really have been allowed some of the checks, or this proves nothing");
        _output.WriteLine($"{allowedBefore} allowed checks preserved. {s.ExecutionMarker}");
    }

    [Fact]
    public async Task Directory_search_team_management_and_test_connect_are_granted_to_exactly_the_roles_that_could_act_before()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        await SeedMatrixAsync(s);
        var ids = PreservationRoles.Where(id => id is not ("r-unreadable" or "r-unreadable-element")).ToArray();
        var before = await LoadRolesAsync(s, ids);
        await using (var db = s.NewDb())
            await RoleAuthorizationDataStep.RunAsync(db);
        var after = await LoadRolesAsync(s, ids);

        var scope = new ScopeContainmentEvaluator();
        var problems = new List<string>();
        for (var i = 0; i < before.Count; i++)
        {
            var old = new[] { before[i] };
            var set = EffectivePermissionSet.FromRoles([after[i]], scope);
            bool Has(string action, string type) => set.Allows(new PermissionDescriptor(action, new ResourceDescriptor(type, "instance")), "instance");

            var couldSearch = PreStepAuthorizationReference.Allows(old, "auth.configuration.read", "InstanceConfiguration")
                              || PreStepAuthorizationReference.Allows(old, "auth.entra.manage", "InstanceConfiguration");
            if (couldSearch != Has("auth.directory.search", "InstanceConfiguration"))
                problems.Add($"{ids[i]}: directory search before {couldSearch}");

            var couldVerify = PreStepAuthorizationReference.Allows(old, "connection.read", "Connection");
            if (couldVerify != Has("connection.verify", "Connection"))
                problems.Add($"{ids[i]}: test-connect before {couldVerify}");

            // Team management: the explicit schedule.manage on Schedule holders. A broad grant never had it as a recipient.
            var couldManageTeams = after[i].Permissions.Any(p => p.Action == "schedule.manage" && p.Resource is { ResourceType: "Schedule", ScopeKey: "instance" });
            if (couldManageTeams != Has("operational_team.manage", "OperationalTeam"))
                problems.Add($"{ids[i]}: team management before {couldManageTeams}");
        }

        problems.Should().BeEmpty();
        _output.WriteLine(s.ExecutionMarker);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The obsolete read on Role at every scope (the same rule as the administrator repair)
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task The_obsolete_read_on_Role_is_removed_at_every_scope_from_readable_custom_roles_only_and_the_summary_is_exact(bool rcsi)
    {
        await using var s = await NewCatalogAsync(rcsi);
        const string unreadableJson = """[null,{"action":"user.read","resource":{"resourceType":"Role","scopeKey":"module:X"}}]""";
        await AddRoleAsync(s, "r-ob-scopes", Json(
            G("user.read", "Role"), G("user.read", "Role", "module:X"), G("user.read", "Role", "environment:local"),
            G("user.read", "User"), G("user.read", "User", "module:X"), G("integration.read", "Integration")));
        await AddRoleAsync(s, "r-ob-dup", Json(G("user.read", "Role"), G("user.read", "Role"), G("connection.browse", "Connection")));
        await AddRoleAsync(s, "r-ob-broad", Json(G("user.read"), G("user.read", "Role", "module:Y")));
        await AddRoleAsync(s, "r-ob-unreadable", unreadableJson);
        await AddRoleAsync(s, "r-ob-system", Json(G("user.read", "Role", "module:X")), system: true);
        await AddRoleAsync(s, "r-ob-clean", Json(G("user.read", "User")));
        var systemBefore = await StoredJsonAsync(s, "r-ob-system");
        var cleanVersion = await s.ScalarAsync<long>("SELECT CONVERT(bigint, RowVersion) FROM Roles WHERE Id = N'r-ob-clean';");

        Report report;
        await using (var db = s.NewDb())
            report = await RoleAuthorizationDataStep.RunAsync(db);

        (await IdentitiesAsync(s, "r-ob-scopes")).Should().Equal(Expected("user.read@User:instance", "user.read@User:module:X", "integration.read@Integration:instance"));
        (await IdentitiesAsync(s, "r-ob-dup")).Should().Equal(Expected("connection.browse@Connection:instance"));
        (await IdentitiesAsync(s, "r-ob-broad")).Should().Equal(Expected("user.read"), "the broad grant is never changed");
        (await StoredJsonAsync(s, "r-ob-unreadable")).Should().Be(unreadableJson, "unreadable data is left exactly as stored");
        (await StoredJsonAsync(s, "r-ob-system")).Should().Be(systemBefore, "a system role is the repair's, not the step's");
        (await s.ScalarAsync<long>("SELECT CONVERT(bigint, RowVersion) FROM Roles WHERE Id = N'r-ob-clean';")).Should().Be(cleanVersion, "a role with nothing obsolete is not written");

        report.Committed.Should().BeTrue();
        report.RolesChanged.Should().Be(3);
        report.GrantsRemoved.Should().Be(5, "three scopes, one identical pair counted once, one module scope");
        report.GrantsAdded.Should().Be(0);

        var rows = await s.RowsAsync($"SELECT * FROM AuditLogs WHERE Action = N'{StepAction}';");
        var details = JsonDocument.Parse((string)rows.Should().ContainSingle().Subject["Details"]!).RootElement;
        details.GetProperty("rolesChanged").GetInt32().Should().Be(3);
        details.GetProperty("grantsRemoved").GetInt32().Should().Be(5);
        details.GetProperty("grantsAdded").GetInt32().Should().Be(0);
        details.GetProperty("changes").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(
        [
            "r-ob-scopes|-user.read@Role:instance", "r-ob-scopes|-user.read@Role:module:X", "r-ob-scopes|-user.read@Role:environment:local",
            "r-ob-dup|-user.read@Role:instance", "r-ob-broad|-user.read@Role:module:Y",
        ]);
        details.GetProperty("changedRoles").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["r-ob-scopes", "r-ob-dup", "r-ob-broad"]);
        details.GetProperty("unreadableRoles").EnumerateArray().Select(e => e.GetString()).Should().Equal("r-ob-unreadable");
        details.GetProperty("broadGrantRoles").EnumerateArray().Select(e => e.GetString()).Should().Equal("r-ob-broad");

        // A second run finds nothing: no change, no second event, no row version moves.
        var snapshot = await s.RowsAsync("SELECT Id, PermissionsJson, CONVERT(bigint, RowVersion) AS Rv FROM Roles ORDER BY Id;");
        Report second;
        await using (var db = s.NewDb())
            second = await RoleAuthorizationDataStep.RunAsync(db);
        second.Committed.Should().BeFalse();
        (await s.RowsAsync("SELECT Id, PermissionsJson, CONVERT(bigint, RowVersion) AS Rv FROM Roles ORDER BY Id;")).Should().BeEquivalentTo(snapshot);
        (await s.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{StepAction}';")).Should().Be(1);
        _output.WriteLine(s.ExecutionMarker);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Idempotency, startup and failure
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task A_second_run_changes_nothing_and_writes_no_second_event(bool rcsi)
    {
        await using var s = await NewCatalogAsync(rcsi);
        await SeedMatrixAsync(s);
        await using (var db = s.NewDb())
            await RoleAuthorizationDataStep.RunAsync(db);
        var snapshot = await s.RowsAsync("SELECT Id, PermissionsJson, CONVERT(bigint, RowVersion) AS Rv FROM Roles ORDER BY Id;");

        Report second;
        await using (var db = s.NewDb())
            second = await RoleAuthorizationDataStep.RunAsync(db);

        second.Committed.Should().BeFalse();
        second.RolesChanged.Should().Be(0);
        (await s.RowsAsync("SELECT Id, PermissionsJson, CONVERT(bigint, RowVersion) AS Rv FROM Roles ORDER BY Id;")).Should().BeEquivalentTo(snapshot, "not even a row version moves");
        (await s.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{StepAction}';")).Should().Be(1);
        _output.WriteLine(s.ExecutionMarker);
    }

    [Fact]
    public async Task A_catalog_with_nothing_to_change_gets_no_event()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        await AddRoleAsync(s, "r-plain", Json(G("integration.read", "Integration")));

        Report report;
        await using (var db = s.NewDb())
            report = await RoleAuthorizationDataStep.RunAsync(db);

        report.Committed.Should().BeFalse();
        (await s.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{StepAction}';")).Should().Be(0);
        _output.WriteLine(s.ExecutionMarker);
    }

    [Fact]
    public async Task A_host_start_runs_the_step_and_a_second_start_changes_nothing()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        await AddRoleAsync(s, "r-start", Json(G("role.manage", "Role")));

        await s.MigrateToCurrentAsync();
        (await IdentitiesAsync(s, "r-start")).Should().Equal(Expected([RoleManageId, .. Eight, DirectorySearchId]));
        await s.MigrateToCurrentAsync();

        (await s.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{StepAction}';")).Should().Be(1);
        _output.WriteLine(s.ExecutionMarker);
    }

    [Fact]
    public async Task A_failure_rolls_the_whole_step_back_and_reaches_the_caller_so_the_host_does_not_start()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        await AddRoleAsync(s, "r-one", Json(G("role.manage", "Role")));
        await AddRoleAsync(s, "r-two", Json(G("schedule.manage", "Schedule")));
        var before = await s.RowsAsync("SELECT Id, PermissionsJson FROM Roles ORDER BY Id;");
        await s.ExecuteAsync("CREATE TRIGGER dbo.trg_block_step_audit ON dbo.AuditLogs AFTER INSERT AS BEGIN THROW 51000, 'audit write blocked for the test', 1; END;");

        var act = async () =>
        {
            await using var db = s.NewDb();
            await RoleAuthorizationDataStep.RunAsync(db);
        };
        await act.Should().ThrowAsync<Exception>("a step that cannot complete must fail the start");

        (await s.RowsAsync("SELECT Id, PermissionsJson FROM Roles ORDER BY Id;")).Should().BeEquivalentTo(before, "no role changed without its audit row");
        (await s.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{StepAction}';")).Should().Be(0);

        await s.ExecuteAsync("DROP TRIGGER dbo.trg_block_step_audit;");
        await using (var db = s.NewDb())
            (await RoleAuthorizationDataStep.RunAsync(db)).Committed.Should().BeTrue("the next start completes what the failed one did not");
        (await IdentitiesAsync(s, "r-two")).Should().Equal(Expected(ScheduleId, TeamId));
        _output.WriteLine(s.ExecutionMarker);
    }

    [Fact]
    public async Task A_migrator_start_fails_when_the_step_cannot_complete()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        await AddRoleAsync(s, "r-one", Json(G("role.manage", "Role")));
        await s.ExecuteAsync("CREATE TRIGGER dbo.trg_block_step_audit ON dbo.AuditLogs AFTER INSERT AS BEGIN THROW 51000, 'audit write blocked for the test', 1; END;");

        var act = async () => await s.MigrateToCurrentAsync();

        await act.Should().ThrowAsync<Exception>();
        (await IdentitiesAsync(s, "r-one")).Should().Equal(Expected(RoleManageId));
        _output.WriteLine(s.ExecutionMarker);
    }

    [Fact]
    public async Task Two_hosts_starting_together_change_each_role_once_and_write_one_event()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        for (var i = 0; i < 20; i++)
            await AddRoleAsync(s, $"r-race-{i:00}", Json(G("role.manage", "Role"), G("schedule.manage", "Schedule")));

        var runs = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var db = s.NewDb();
            return await RoleAuthorizationDataStep.RunAsync(db);
        }));

        runs.Count(r => r.Committed).Should().Be(1, "the update locks serialize the runs, so only the first has anything to change");
        (await s.ScalarAsync<int>($"SELECT COUNT(*) FROM AuditLogs WHERE Action = N'{StepAction}';")).Should().Be(1);
        for (var i = 0; i < 20; i++)
            (await IdentitiesAsync(s, $"r-race-{i:00}")).Should().Equal(Expected([RoleManageId, .. Eight, DirectorySearchId, ScheduleId, TeamId]));
        _output.WriteLine(s.ExecutionMarker);
    }

    [Fact]
    public async Task A_large_instance_keeps_exact_counts_and_a_bounded_event()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        for (var i = 0; i < 150; i++)
            await AddRoleAsync(s, $"r-bulk-{i:000}", Json(G("role.manage", "Role"), G("connection.read", "Connection")));

        Report report;
        await using (var db = s.NewDb())
            report = await RoleAuthorizationDataStep.RunAsync(db);

        report.RolesChanged.Should().Be(150);
        var row = (await s.RowsAsync($"SELECT Details FROM AuditLogs WHERE Action = N'{StepAction}';")).Single();
        var text = (string)row["Details"]!;
        var details = JsonDocument.Parse(text).RootElement;
        details.GetProperty("rolesChanged").GetInt32().Should().Be(150);
        details.GetProperty("grantsAdded").GetInt32().Should().Be(150 * (8 + 1 + 1));
        details.GetProperty("changedRoles").GetArrayLength().Should().Be(100, "the list is bounded; the count is exact");
        details.GetProperty("changes").GetArrayLength().Should().Be(100);
        System.Text.Encoding.UTF8.GetByteCount(text).Should().BeLessThan(32 * 1024);
        _output.WriteLine(s.ExecutionMarker);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The step does not widen: nothing the old rule denied becomes allowed
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task After_the_step_no_role_can_do_any_of_the_ten_checks_it_could_not_do_before()
    {
        await using var s = await NewCatalogAsync(rcsi: false);
        await SeedMatrixAsync(s);
        var before = await LoadRolesAsync(s, PreservationRoles);
        await using (var db = s.NewDb())
            await RoleAuthorizationDataStep.RunAsync(db);
        var after = await LoadRolesAsync(s, PreservationRoles);

        var scope = new ScopeContainmentEvaluator();
        var widened = new List<string>();
        for (var i = 0; i < before.Count; i++)
        {
            var set = EffectivePermissionSet.FromRoles([after[i]], scope);
            foreach (var (action, type) in PreStepAuthorizationReference.TenCombinations)
            {
                if (set.Allows(new PermissionDescriptor(action, new ResourceDescriptor(type, "instance")), "instance")
                    && !PreStepAuthorizationReference.Allows([before[i]], action, type))
                    widened.Add($"{before[i].Id.Value}: {action}@{type}");
            }
        }

        widened.Should().BeEmpty();
        _output.WriteLine(s.ExecutionMarker);
    }
}
