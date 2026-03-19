using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Management.Api.Controllers;
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

    private sealed class StubUserManagementService : IUserManagementService
    {
        private readonly Exception? _setThrows;
        private readonly Exception? _getPermissionsThrows;

        public StubUserManagementService(Exception? setThrows = null, Exception? getPermissionsThrows = null)
        {
            _setThrows = setThrows;
            _getPermissionsThrows = getPermissionsThrows;
        }

        public ValueTask<(IReadOnlyList<Apperture.Core.Domain.Identity.User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(((IReadOnlyList<Apperture.Core.Domain.Identity.User>)([]), 0));

        public ValueTask<Apperture.Core.Domain.Identity.User?> GetUserByIdAsync(Apperture.Core.Domain.Identity.UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Apperture.Core.Domain.Identity.User?>(null);

        public ValueTask<IReadOnlyList<Apperture.Core.Domain.Identity.Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Apperture.Core.Domain.Identity.Role>>([]);

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
        }

        public ValueTask<Apperture.Core.Domain.Identity.UserId> CreateUserAsync(string displayName, Apperture.Core.Domain.Identity.IdentitySource source, string externalId, string? password = null, CancellationToken cancellationToken = default) =>
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
    }
}
