using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// Epic 39: Governance admin surface — principal effective access, etc.
/// </summary>
public sealed class Epic39GovernanceAdminTests
{
    [Fact]
    public async Task GetEffectiveAccess_WhenUserExistsWithRoles_ReturnsMergedPermissions()
    {
        var user = new User(
            new UserId("u1"),
            "Alice",
            [new IdentityLink(IdentitySource.Local, "alice", null)],
            [new RoleId("r1")],
            false)
        { Username = "alice" };
        var perm = new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance"));
        var role = new Role(new RoleId("r1"), "Admin", null, [perm], false);
        var userManagement = new StubUserManagement(user);
        var roleProvider = new StubRoleProvider([role]);

        var controller = new UsersController(userManagement, new StubLocalCredentialRepository(), roleProvider);

        var result = await controller.GetEffectiveAccess("u1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (PrincipalEffectiveAccessDto)ok.Value!;
        dto.UserId.Should().Be("u1");
        dto.RoleIds.Should().ContainSingle("r1");
        dto.Permissions.Should().ContainSingle(p => p.Action == "user.read");
    }

    [Fact]
    public async Task GetEffectiveAccess_WhenUserNotFound_Returns404()
    {
        var userManagement = new StubUserManagement(null);
        var roleProvider = new StubRoleProvider([]);

        var controller = new UsersController(userManagement, new StubLocalCredentialRepository(), roleProvider);

        var result = await controller.GetEffectiveAccess("nonexistent", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetEffectiveAccess_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagement(throws: new UnauthorizedAccessException("deny"));
        var roleProvider = new StubRoleProvider([]);

        var controller = new UsersController(userManagement, new StubLocalCredentialRepository(), roleProvider);

        var result = await controller.GetEffectiveAccess("u1", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetEffectiveAccess_WhenUserHasMultipleRoles_MergesAndDeduplicatesPermissions()
    {
        var user = new User(
            new UserId("u1"),
            "Alice",
            [new IdentityLink(IdentitySource.Local, "alice", null)],
            [new RoleId("r1"), new RoleId("r2")],
            false)
        { Username = "alice" };
        var perm1 = new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance"));
        var perm2 = new PermissionDescriptor("audit.read", new ResourceDescriptor("AuditPolicy", "instance"));
        var role1 = new Role(new RoleId("r1"), "Admin", null, [perm1], false);
        var role2 = new Role(new RoleId("r2"), "Auditor", null, [perm1, perm2], false);
        var userManagement = new StubUserManagement(user);
        var roleProvider = new StubRoleProvider([role1, role2]);

        var controller = new UsersController(userManagement, new StubLocalCredentialRepository(), roleProvider);

        var result = await controller.GetEffectiveAccess("u1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var dto = (PrincipalEffectiveAccessDto)ok.Value!;
        dto.UserId.Should().Be("u1");
        dto.RoleIds.Should().BeEquivalentTo("r1", "r2");
        dto.Permissions.Should().Contain(p => p.Action == "user.read");
        dto.Permissions.Should().Contain(p => p.Action == "audit.read");
        dto.Permissions.Should().HaveCount(2);
    }

    private sealed class StubUserManagement : IUserManagementService
    {
        private readonly User? _user;
        private readonly Exception? _throws;

        public StubUserManagement(User? user = null, Exception? throws = null)
        {
            _user = user;
            _throws = throws;
        }

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(((IReadOnlyList<User>)([]), 0));

        public ValueTask<User?> GetUserByIdAsync(UserId userId, CancellationToken cancellationToken = default)
        {
            if (_throws is not null)
                throw _throws;
            return ValueTask.FromResult(_user);
        }

        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<PermissionDescriptor>> GetRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PermissionDescriptor>>([]);

        public ValueTask SetRolePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<UserId> CreateUserAsync(
            string displayName,
            IdentitySource source,
            string externalId,
            string? password = null,
            CancellationToken cancellationToken = default,
            string? contactEmail = null,
            bool sendSetupInvitation = false) =>
            throw new NotImplementedException();

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<EmergencyAccessPosture> GetEmergencyAccessPostureAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubRoleProvider : IRoleProvider
    {
        private readonly IReadOnlyList<Role> _roles;

        public StubRoleProvider(IReadOnlyList<Role> roles) => _roles = roles;

        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_roles);

        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class StubLocalCredentialRepository : ILocalCredentialRepository
    {
        public ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LocalCredentialData?>(null);

        public ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
