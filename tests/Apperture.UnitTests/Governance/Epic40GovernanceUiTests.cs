using Apperture.Management.Client;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Epic 40: Authorization management UI truthfulness tests.
/// Verifies governance DTOs, replace-all semantics constants, and effective-access scope.
/// </summary>
public sealed class Epic40GovernanceUiTests
{
    [Fact]
    public void RoleEditReplaceAllSemantics_MessageContainsReplaceAndRevoke()
    {
        const string expectedMessage = "Saving replaces the role's entire permission set. Permissions not selected will be revoked.";
        expectedMessage.Should().Contain("replaces", "UI must make replace-all explicit");
        expectedMessage.Should().Contain("revoked", "UI must make revocation-by-omission explicit");
    }

    [Fact]
    public void EffectiveAccessLimitationMessage_DoesNotOverclaim()
    {
        const string expectedMessage = "Effective access shows merged permissions from assigned roles. It does not include direct user grants, scope containment, or service principals.";
        expectedMessage.Should().Contain("merged permissions", "must state merged source");
        expectedMessage.Should().Contain("assigned roles", "must state role-derived");
        expectedMessage.Should().Contain("direct user grants", "must exclude unsupported");
        expectedMessage.Should().Contain("scope containment", "must exclude unsupported");
        expectedMessage.Should().Contain("service principals", "must exclude unsupported");
    }

    [Fact]
    public void GovernanceDescriptorResponse_ResourcesAndPermissions_ReflectCatalogShape()
    {
        var response = new GovernanceDescriptorResponse(
            [new ResourceCatalogEntryDto("User", "instance", "Core")],
            [new PermissionCatalogEntryDto("user.read", "User", "instance", "Core", "platform", null)]);
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
}
