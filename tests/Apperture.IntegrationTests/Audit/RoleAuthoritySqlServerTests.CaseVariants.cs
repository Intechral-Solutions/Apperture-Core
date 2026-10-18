using System.Text.Json;
using Apperture.ControlPlane.Security;
using Apperture.Core.Domain.Identity;
using FluentAssertions;
using Xunit;
using static Apperture.IntegrationTests.Audit.RoleAuthorityWorld;

namespace Apperture.IntegrationTests.Audit;

// IAM-10.2a review remediation (M-1): a role id typed in another case finds the same row on a case-insensitive catalog, so every
// decision and every record that follows must use the id of the locked row, never the caller's spelling. These run on a fresh
// catalog each (the last-administrator answer depends on how many administrators there are, and the audit table is append-only),
// on both isolation modes.
public sealed partial class RoleAuthoritySqlServerTests
{
    private const string BlockedLastAdmin = "auth_admin.blocked.last_platform_admin_removal";

    public static IEnumerable<object[]> CaseVariantsOnBothModes() =>
        from variant in new[] { "ROLE-PLATFORM-ADMINISTRATOR", "Role-Platform-Administrator", "role-PLATFORM-administrator" }
        from rcsi in new[] { false, true }
        select new object[] { variant, rcsi };

    private static async Task<bool> StoredAsExactlyAsync(RoleAuthorityWorld world, string userId, string roleId) =>
        await world.Scratch.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM UserRoles WHERE UserId = N'{userId}' AND RoleId COLLATE Latin1_General_BIN2 = N'{roleId}';") == 1;

    private static async Task<string?> RoleIdInAuditRowAsync(RoleAuthorityWorld world, string action, string targetId)
    {
        var rows = await world.AuditRowDetailsAsync(action, targetId);
        var details = rows.Should().ContainSingle().Subject["Details"]!.ToString()!;
        using var json = JsonDocument.Parse(details);
        return json.RootElement.GetProperty("roleId").GetString();
    }

    [Theory]
    [MemberData(nameof(CaseVariantsOnBothModes))]
    public async Task M1_removing_the_last_administrator_with_a_case_variant_of_the_role_id_is_refused_like_the_exact_id(string variant, bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_IamM1Last_", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        var sole = await world.NewUserAsync("sole", AdminRoleId);

        await using var db = scratch.NewDb();
        var act = () => world.Service(db, sole).RemoveRoleAsync(new UserId(sole), new RoleId(variant)).AsTask();

        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be("last_viable_administrator");
        (await world.AssignmentExistsAsync(sole, AdminRoleId)).Should().BeTrue("the last administrator keeps the role");
        (await StoredAsExactlyAsync(world, sole, AdminRoleId)).Should().BeTrue();
        (await world.AuditRowsAsync(BlockedLastAdmin, sole)).Should().Be(1, "one refusal row");
        (await RoleIdInAuditRowAsync(world, BlockedLastAdmin, sole)).Should().Be(AdminRoleId, "the refusal names the role by its stored id");
        (await world.AuditRowsAsync(Removed, sole)).Should().Be(0);
        _output.WriteLine(scratch.ExecutionMarker);
    }

    [Theory]
    [MemberData(nameof(CaseVariantsOnBothModes))]
    public async Task M1_removing_one_of_two_administrators_with_a_case_variant_succeeds_and_records_the_stored_role_id(string variant, bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_IamM1Two_", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        var first = await world.NewUserAsync("first", AdminRoleId);
        var second = await world.NewUserAsync("second", AdminRoleId);

        await using (var db = scratch.NewDb())
            await world.Service(db, first).RemoveRoleAsync(new UserId(second), new RoleId(variant));

        (await world.AssignmentExistsAsync(second, AdminRoleId)).Should().BeFalse();
        (await world.AssignmentExistsAsync(first, AdminRoleId)).Should().BeTrue();
        (await RoleIdInAuditRowAsync(world, Removed, second)).Should().Be(AdminRoleId, "the audit row records the id of the role, not the caller's spelling");
        (await world.AuditRowsAsync(BlockedLastAdmin, second)).Should().Be(0);

        // The one left is now the last: the exact id and a variant are refused alike.
        await using var db2 = scratch.NewDb();
        var act = () => world.Service(db2, first).RemoveRoleAsync(new UserId(first), new RoleId(variant)).AsTask();
        (await act.Should().ThrowAsync<GovernanceConflictException>()).Which.Code.Should().Be("last_viable_administrator");
        (await world.AssignmentExistsAsync(first, AdminRoleId)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(CaseVariantsOnBothModes))]
    public async Task M1_assigning_a_role_the_user_already_holds_by_a_case_variant_is_a_silent_no_op_and_stores_nothing_new(string variant, bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_IamM1Idem_", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        var administrator = await world.NewUserAsync("administrator", AdminRoleId);
        var other = await world.NewUserAsync("other", AdminRoleId);

        await using (var db = scratch.NewDb())
            await world.Service(db, administrator).AssignRoleAsync(new UserId(other), new RoleId(variant));

        (await world.AssignmentCountAsync(other, AdminRoleId)).Should().Be(1);
        (await StoredAsExactlyAsync(world, other, AdminRoleId)).Should().BeTrue("the stored assignment is untouched");
        (await world.AuditRowsAsync(Assigned, other)).Should().Be(0, "an assignment that already exists records nothing");
        (await world.AuditRowsAsync(AssignmentRejected, other)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(CaseVariantsOnBothModes))]
    public async Task M1_a_new_assignment_by_a_case_variant_stores_and_records_the_canonical_role_id(string variant, bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_IamM1New_", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        var administrator = await world.NewUserAsync("administrator", AdminRoleId);
        var promoted = await world.NewUserAsync("promoted");

        await using (var db = scratch.NewDb())
            await world.Service(db, administrator).AssignRoleAsync(new UserId(promoted), new RoleId(variant));

        (await StoredAsExactlyAsync(world, promoted, AdminRoleId)).Should().BeTrue("the assignment row carries the id of the role, so every later comparison finds it");
        (await RoleIdInAuditRowAsync(world, Assigned, promoted)).Should().Be(AdminRoleId);
    }

    [Theory]
    [MemberData(nameof(BothModes))]
    public async Task M1_a_custom_role_assigned_and_removed_by_a_case_variant_is_stored_and_recorded_by_its_own_id(bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_IamM1Custom_", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        var administrator = await world.NewUserAsync("administrator", AdminRoleId);
        var role = await world.NewRoleAsync("custom", P);
        var holder = await world.NewUserAsync("holder");
        var variant = role.ToUpperInvariant();

        await using (var db = scratch.NewDb())
            await world.Service(db, administrator).AssignRoleAsync(new UserId(holder), new RoleId(variant));
        (await StoredAsExactlyAsync(world, holder, role)).Should().BeTrue();
        (await RoleIdInAuditRowAsync(world, Assigned, holder)).Should().Be(role);

        await using (var db = scratch.NewDb())
            await world.Service(db, administrator).RemoveRoleAsync(new UserId(holder), new RoleId(variant));
        (await world.AssignmentExistsAsync(holder, role)).Should().BeFalse("an ordinary role, even its only holder, is removable");
        (await RoleIdInAuditRowAsync(world, Removed, holder)).Should().Be(role);
        (await world.AuditRowsAsync(BlockedLastAdmin, holder)).Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(CaseVariantsOnBothModes))]
    public async Task M1_a_non_administrator_is_refused_the_administrator_role_by_a_case_variant_even_when_the_request_would_change_nothing(string variant, bool rcsi)
    {
        await using var scratch = await AuditIsolation.CreateAsync("Apperture_IamM1Deny_", rcsi);
        var world = new RoleAuthorityWorld(scratch);
        var escalator = await EscalatorAsync(world);
        var alreadyAdmin = await world.NewUserAsync("already", AdminRoleId);
        var notAdmin = await world.NewUserAsync("notadmin");

        foreach (var (target, removal) in new[] { (alreadyAdmin, false), (notAdmin, false), (alreadyAdmin, true), (notAdmin, true) })
        {
            await using var db = scratch.NewDb();
            var service = world.Service(db, escalator);
            var act = () => (removal ? service.RemoveRoleAsync(new UserId(target), new RoleId(variant)) : service.AssignRoleAsync(new UserId(target), new RoleId(variant))).AsTask();

            (await act.Should().ThrowAsync<AuthorizationRefusalException>()).Which.Code.Should().Be("platform_administrator_restricted");
        }

        (await world.AssignmentExistsAsync(alreadyAdmin, AdminRoleId)).Should().BeTrue();
        (await world.AssignmentExistsAsync(notAdmin, AdminRoleId)).Should().BeFalse();
        (await world.AuditRowsAsync(AssignmentRejected, alreadyAdmin)).Should().Be(2);
        (await world.AuditRowsAsync(AssignmentRejected, notAdmin)).Should().Be(2);
        (await world.AuditRowsAsync(Assigned, notAdmin)).Should().Be(0);
    }
}
