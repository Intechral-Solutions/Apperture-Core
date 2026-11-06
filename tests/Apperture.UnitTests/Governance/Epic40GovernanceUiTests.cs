using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Epic 40: Authorization management UI truthfulness tests.
/// Verifies governance DTOs and the system-role notice. The replace-all role-edit notice and the role-derived access
/// limitation notice are asserted against the rendered pages in Management.Ui.Client.Tests (Epic40GovernanceNoticeUiTests).
/// </summary>
public sealed class Epic40GovernanceUiTests
{
    [Fact]
    public void SystemRoleReadOnlyNotice_ExplainsNonEditability()
    {
        var detail = File.ReadAllText(Path.Combine(RepoRoot(), "src/Apperture.ControlPlane/Apperture.Management.Ui.Client/Pages/Governance/RoleDetail.razor"));
        const string expectedNotice = "This built-in role cannot be changed or deleted.";
        detail.Should().Contain(expectedNotice);

        // The sentence also lives in a code-behind constant used for toasts and errors, so the check above passes even when the
        // notice markup is gone. Read the notice element itself.
        var marker = detail.IndexOf("id=\"role-system-notice\"", StringComparison.Ordinal);
        marker.Should().BeGreaterThanOrEqualTo(0, "the read-only notice element must exist in the markup");
        var start = detail.IndexOf('>', marker) + 1;
        var element = detail[start..detail.IndexOf("</div>", start, StringComparison.Ordinal)];
        element.Should().Contain(expectedNotice);
        element.Should().Contain("cannot be changed");
        element.Should().Contain("deleted");
    }

    [Fact]
    public void GovernanceDescriptorResponse_ResourcesAndPermissions_ReflectCatalogShape()
    {
        var response = new GovernanceDescriptorResponse(
            [new ResourceCatalogEntryDto("User", "instance", "Core")],
            [new PermissionCatalogEntryDto(
                "user.read", "User", "instance", "Core", "platform", null,
                "View users", "View users, their roles and how they sign in.", "AccessControl", "Standard", false)]);
        response.Resources.Should().ContainSingle();
        response.Resources[0].ResourceType.Should().Be("User");
        response.Resources[0].Family.Should().Be("Core");
        response.Permissions.Should().ContainSingle();
        response.Permissions[0].Action.Should().Be("user.read");
        response.Permissions[0].Source.Should().Be("platform");
    }

    [Fact]
    public void RoleDetailDto_IsSystemRole_ReflectsBackend()
    {
        var role = new RoleDetailDto(
            "r1", "Admin", "System admin", true,
            [new RolePermissionEntryDto("user.read", "User", "instance")]);
        role.IsSystemRole.Should().BeTrue();
        role.Permissions.Should().ContainSingle(p => p.Action == "user.read");
    }

    [Fact]
    public void PrincipalEffectiveAccessDto_RoleIdsAndPermissions_ReflectMergedShape()
    {
        var access = new PrincipalEffectiveAccessDto(
            "u1",
            ["r1", "r2"],
            [
                new RolePermissionEntryDto("user.read", "User", "instance"),
                new RolePermissionEntryDto("audit.read", "AuditPolicy", "instance")
            ]);
        access.UserId.Should().Be("u1");
        access.RoleIds.Should().BeEquivalentTo("r1", "r2");
        access.Permissions.Should().HaveCount(2);
    }

    [Fact]
    public void SetRolePermissionsRequest_FullSet_SentToBackend()
    {
        var request = new SetRolePermissionsRequest([
            new RolePermissionDto("user.read", "User", "instance"),
            new RolePermissionDto("audit.read", "AuditPolicy", "instance")
        ]);
        request.Permissions.Should().HaveCount(2);
        request.Permissions.Should().Contain(p => p.Action == "user.read");
        request.Permissions.Should().Contain(p => p.Action == "audit.read");
    }

    private static string RepoRoot() => RepositoryRoot.Find();
}
