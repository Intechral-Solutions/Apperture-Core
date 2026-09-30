using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Roles API: GetPermissions, SetPermissions, validation behavior.
/// </summary>
public sealed class RolesControllerTests
{
    [Fact]
    public async Task SetPermissions_WhenUnknownPermission_Returns400()
    {
        var userManagement = new StubUserManagementService(setThrows: new InvalidOperationException(
            "Permission at index 0 is not in the canonical governance catalog: Action=unknown.action, Resource=UnknownType:instance."));
        var controller = new RolesController(userManagement);
        var request = new SetRolePermissionsRequest([
            new RolePermissionDto("unknown.action", "UnknownType", "instance")
        ]);

        var result = await controller.SetPermissions("role-1", request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetPermissions_WhenMalformedPermission_Returns400()
    {
        var userManagement = new StubUserManagementService(setThrows: new InvalidOperationException(
            "Permission at index 0 is malformed: ResourceType must not have leading or trailing whitespace."));
        var controller = new RolesController(userManagement);
        var request = new SetRolePermissionsRequest([
            new RolePermissionDto("user.read", " User ", "instance")
        ]);

        var result = await controller.SetPermissions("role-1", request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetPermissions_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(setThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);
        var request = new SetRolePermissionsRequest([new RolePermissionDto("user.read", "User", "instance")]);

        var result = await controller.SetPermissions("role-1", request, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task SetPermissions_WhenSystemRole_Returns409()
    {
        var userManagement = new StubUserManagementService(setThrows: new RoleLifecycleException(
            RoleLifecycleErrorCodes.SystemRole,
            "This built-in role cannot be changed or deleted."));
        var controller = new RolesController(userManagement);
        var request = new SetRolePermissionsRequest([new RolePermissionDto("user.read", "User", "instance")]);

        var result = await controller.SetPermissions("role-platform-administrator", request, CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new
        {
            error = RoleLifecycleErrorCodes.SystemRole,
            message = "This built-in role cannot be changed or deleted.",
            assignmentCount = (int?)null
        });
    }

    [Fact]
    public async Task SetPermissions_WhenValid_Succeeds()
    {
        var userManagement = new StubUserManagementService();
        var controller = new RolesController(userManagement);
        var request = new SetRolePermissionsRequest([new RolePermissionDto("user.read", "User", "instance")]);

        var result = await controller.SetPermissions("role-1", request, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task SetPermissions_WhenRequestPermissionsNull_Returns400()
    {
        var userManagement = new StubUserManagementService();
        var controller = new RolesController(userManagement);

        var result = await controller.SetPermissions("role-1", new SetRolePermissionsRequest(null!), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetPermissions_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(getPermissionsThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);

        var result = await controller.GetPermissions("role-1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task SetPermissions_WhenRoleNotFound_Returns404()
    {
        var userManagement = new StubUserManagementService(setThrows: new InvalidOperationException("Role role-999 not found."));
        var controller = new RolesController(userManagement);
        var request = new SetRolePermissionsRequest([new RolePermissionDto("user.read", "User", "instance")]);

        var result = await controller.SetPermissions("role-999", request, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task List_ReturnsRolesWithIsSystemRole()
    {
        var roles = new[]
        {
            new Apperture.Core.Domain.Identity.Role(
                new Apperture.Core.Domain.Identity.RoleId("r1"),
                "Admin",
                "System admin",
                [],
                true),
        };
        var userManagement = new StubUserManagementService(roles: roles);
        var controller = new RolesController(userManagement);

        var result = await controller.List(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var list = (IReadOnlyList<RoleDto>)ok.Value!;
        list.Should().ContainSingle(r => r.Id == "r1" && r.IsSystemRole);
    }

    [Fact]
    public async Task Get_WhenRoleExists_ReturnsRoleDetailWithGrants()
    {
        var perm = new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance"));
        var role = new Apperture.Core.Domain.Identity.Role(
            new Apperture.Core.Domain.Identity.RoleId("r1"),
            "Admin",
            "System admin",
            [perm],
            true);
        var userManagement = new StubUserManagementService(role: role);
        var controller = new RolesController(userManagement);

        var result = await controller.Get("r1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var detail = (RoleDetailDto)ok.Value!;
        detail.Id.Should().Be("r1");
        detail.Name.Should().Be("Admin");
        detail.IsSystemRole.Should().BeTrue();
        detail.Permissions.Should().ContainSingle(p => p.Action == "user.read");
    }

    [Fact]
    public async Task Get_WhenRoleNotFound_Returns404()
    {
        var userManagement = new StubUserManagementService(role: null);
        var controller = new RolesController(userManagement);

        var result = await controller.Get("nonexistent", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task List_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(getRolesThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);

        var result = await controller.List(CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Get_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(getRoleThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);

        var result = await controller.Get("r1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task SetPermissions_ReplacesFullPermissionSet_RevocationByOmission()
    {
        IReadOnlyList<PermissionDescriptor>? lastPermissions = null;
        var userManagement = new StubUserManagementService(onSetPermissions: perms => lastPermissions = perms);
        var controller = new RolesController(userManagement);

        await controller.SetPermissions("role-1", new SetRolePermissionsRequest([
            new RolePermissionDto("user.read", "User", "instance"),
            new RolePermissionDto("audit.read", "AuditPolicy", "instance")
        ]), CancellationToken.None);
        lastPermissions.Should().HaveCount(2);

        await controller.SetPermissions("role-1", new SetRolePermissionsRequest([
            new RolePermissionDto("user.read", "User", "instance")
        ]), CancellationToken.None);
        lastPermissions.Should().ContainSingle(p => p.Action == "user.read");
        lastPermissions.Should().NotContain(p => p.Action == "audit.read");
    }

    private sealed class StubUserManagementService : IUserManagementService
    {
        private readonly Exception? _setThrows;
        private readonly Exception? _getPermissionsThrows;
        private readonly Exception? _getRolesThrows;
        private readonly Exception? _getRoleThrows;
        private readonly IReadOnlyList<Apperture.Core.Domain.Identity.Role> _roles;
        private readonly Apperture.Core.Domain.Identity.Role? _role;
        private readonly Action<IReadOnlyList<PermissionDescriptor>>? _onSetPermissions;

        public StubUserManagementService(
            Exception? setThrows = null,
            Exception? getPermissionsThrows = null,
            Exception? getRolesThrows = null,
            Exception? getRoleThrows = null,
            IReadOnlyList<Apperture.Core.Domain.Identity.Role>? roles = null,
            Apperture.Core.Domain.Identity.Role? role = null,
            Action<IReadOnlyList<PermissionDescriptor>>? onSetPermissions = null)
        {
            _setThrows = setThrows;
            _getPermissionsThrows = getPermissionsThrows;
            _getRolesThrows = getRolesThrows;
            _getRoleThrows = getRoleThrows;
            _roles = roles ?? [];
            _role = role;
            _onSetPermissions = onSetPermissions;
        }

        public ValueTask<(IReadOnlyList<Apperture.Core.Domain.Identity.User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(((IReadOnlyList<Apperture.Core.Domain.Identity.User>)([]), 0));

        public ValueTask<Apperture.Core.Domain.Identity.User?> GetUserByIdAsync(Apperture.Core.Domain.Identity.UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Domain.Identity.User?>(null);

        public ValueTask<IReadOnlyList<Apperture.Core.Domain.Identity.Role>> GetRolesAsync(CancellationToken cancellationToken = default)
        {
            if (_getRolesThrows is not null)
                throw _getRolesThrows;
            return ValueTask.FromResult<IReadOnlyList<Apperture.Core.Domain.Identity.Role>>(_roles);
        }

        public ValueTask<Apperture.Core.Domain.Identity.Role?> GetRoleAsync(Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default)
        {
            if (_getRoleThrows is not null)
                throw _getRoleThrows;
            return ValueTask.FromResult<Apperture.Core.Domain.Identity.Role?>(_role);
        }

        public async ValueTask<IReadOnlyList<PermissionDescriptor>> GetRolePermissionsAsync(Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default)
        {
            if (_getPermissionsThrows is not null)
                throw _getPermissionsThrows;
            return [];
        }

        public async ValueTask SetRolePermissionsAsync(Apperture.Core.Domain.Identity.RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
        {
            if (_setThrows is not null)
                throw _setThrows;
            _onSetPermissions?.Invoke(permissions);
        }

        public ValueTask<Apperture.Core.Domain.Identity.UserId> CreateUserAsync(
            string displayName,
            Apperture.Core.Domain.Identity.IdentitySource source,
            string externalId,
            string? password = null,
            CancellationToken cancellationToken = default,
            string? contactEmail = null,
            bool sendSetupInvitation = false) =>
            throw new NotImplementedException();

        public ValueTask AddIdentityLinkAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveIdentityLinkAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask AssignRoleAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveRoleAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SetLocalPasswordAsync(Apperture.Core.Domain.Identity.UserId userId, string newPassword, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SetLocalCredentialDisabledAsync(Apperture.Core.Domain.Identity.UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<EmergencyAccessPosture> GetEmergencyAccessPostureAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
