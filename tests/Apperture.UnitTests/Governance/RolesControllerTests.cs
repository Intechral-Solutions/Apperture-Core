using System.Reflection;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
        var userManagement = new StubUserManagementService(setThrows: new GovernanceValidationException(
            GovernanceValidationCodes.InvalidPermission,
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
        var userManagement = new StubUserManagementService(setThrows: new GovernanceValidationException(
            GovernanceValidationCodes.InvalidPermission,
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
        var userManagement = new StubUserManagementService(setThrows: new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, "role-999"));
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

    [Fact]
    public async Task Create_WhenAccepted_Returns201WithDetailAndCurrentAssignmentCount()
    {
        var role = SampleRole();
        var userManagement = new StubUserManagementService(
            createdRole: role,
            assignments: new RoleAssignmentList(1, [new RoleAssignmentHolder(new UserId("user-1"), "ada", "Ada")]));
        var controller = new RolesController(userManagement);

        var result = await controller.Create(
            new CreateRoleRequest("Operators", "Desk", [new RolePermissionDto("user.read", "User", "instance")]),
            CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.ActionName.Should().Be(nameof(RolesController.Get));
        created.RouteValues.Should().NotBeNull();
        created.RouteValues!["id"].Should().Be("role-abc");
        var detail = created.Value.Should().BeOfType<RoleDetailDto>().Subject;
        detail.Id.Should().Be("role-abc");
        detail.Name.Should().Be("Operators");
        detail.Description.Should().Be("Desk");
        detail.IsSystemRole.Should().BeFalse();
        detail.AssignmentCount.Should().Be(1);
        detail.Permissions.Should().ContainSingle(p => p.Action == "user.read");
    }

    [Fact]
    public async Task Create_ForwardsNameDescriptionAndPermissionsOnly()
    {
        var userManagement = new StubUserManagementService(createdRole: SampleRole());
        var controller = new RolesController(userManagement);

        await controller.Create(
            new CreateRoleRequest("  Operators  ", "  Desk  ", [new RolePermissionDto("user.read", "User", "instance")]),
            CancellationToken.None);

        userManagement.CreatedName.Should().Be("  Operators  ");
        userManagement.CreatedDescription.Should().Be("  Desk  ");
        userManagement.CreatedPermissions.Should().ContainSingle(p =>
            p.Action == "user.read"
            && p.Resource!.ResourceType == "User"
            && p.Resource.ScopeKey == "instance");
        typeof(CreateRoleRequest).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("Name", "Description", "Permissions");
    }

    [Fact]
    public async Task Create_WhenPermissionsOmitted_ForwardsNullGrants()
    {
        var userManagement = new StubUserManagementService(createdRole: SampleRole());
        var controller = new RolesController(userManagement);

        await controller.Create(new CreateRoleRequest("Operators"), CancellationToken.None);

        userManagement.CreatedPermissions.Should().BeNull();
    }

    [Fact]
    public async Task Create_WhenDuplicateName_Returns409()
    {
        var userManagement = new StubUserManagementService(createThrows: new RoleLifecycleException(
            RoleLifecycleErrorCodes.DuplicateName,
            "A role with this name already exists."));
        var controller = new RolesController(userManagement);

        var result = await controller.Create(new CreateRoleRequest("Operators"), CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new
        {
            error = RoleLifecycleErrorCodes.DuplicateName,
            message = "A role with this name already exists.",
            assignmentCount = (int?)null
        });
    }

    [Fact]
    public async Task Create_WhenInvalid_Returns400()
    {
        var userManagement = new StubUserManagementService(createThrows: new GovernanceValidationException(GovernanceValidationCodes.RoleNameRequired, "Role name is required."));
        var controller = new RolesController(userManagement);

        var result = await controller.Create(new CreateRoleRequest(" "), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new { message = "Role name is required." });
    }

    [Fact]
    public async Task Create_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(createThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);

        var result = await controller.Create(new CreateRoleRequest("Operators"), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task UpdateMetadata_WhenAccepted_Returns200WithCurrentAssignmentCount()
    {
        var role = SampleRole(name: "Renamed", description: null);
        var userManagement = new StubUserManagementService(
            updatedRole: role,
            assignments: new RoleAssignmentList(2, [
                new RoleAssignmentHolder(new UserId("user-1"), "ada", "Ada"),
                new RoleAssignmentHolder(new UserId("user-2"), "grace", "Grace")
            ]));
        var controller = new RolesController(userManagement);

        var result = await controller.UpdateMetadata("role-abc", new UpdateRoleMetadataRequest("Renamed", null), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var detail = ok.Value.Should().BeOfType<RoleDetailDto>().Subject;
        detail.Name.Should().Be("Renamed");
        detail.Description.Should().BeNull();
        detail.AssignmentCount.Should().Be(2);
        userManagement.UpdatedName.Should().Be("Renamed");
        userManagement.UpdatedDescription.Should().BeNull();
        typeof(UpdateRoleMetadataRequest).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("Name", "Description");
    }

    [Fact]
    public async Task UpdateMetadata_WhenSystemRole_Returns409()
    {
        var userManagement = new StubUserManagementService(updateThrows: new RoleLifecycleException(
            RoleLifecycleErrorCodes.SystemRole,
            "This built-in role cannot be changed or deleted."));
        var controller = new RolesController(userManagement);

        var result = await controller.UpdateMetadata(
            "role-platform-administrator",
            new UpdateRoleMetadataRequest("Renamed", "Nope"),
            CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new
        {
            error = RoleLifecycleErrorCodes.SystemRole,
            message = "This built-in role cannot be changed or deleted.",
            assignmentCount = (int?)null
        });
    }

    [Fact]
    public async Task UpdateMetadata_WhenDuplicateName_Returns409()
    {
        var userManagement = new StubUserManagementService(updateThrows: new RoleLifecycleException(
            RoleLifecycleErrorCodes.DuplicateName,
            "A role with this name already exists."));
        var controller = new RolesController(userManagement);

        var result = await controller.UpdateMetadata("role-abc", new UpdateRoleMetadataRequest("Platform Administrator", null), CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new
        {
            error = RoleLifecycleErrorCodes.DuplicateName,
            message = "A role with this name already exists.",
            assignmentCount = (int?)null
        });
    }

    [Fact]
    public async Task UpdateMetadata_WhenMissing_Returns404()
    {
        var userManagement = new StubUserManagementService(updateThrows: new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, "role-missing"));
        var controller = new RolesController(userManagement);

        var result = await controller.UpdateMetadata("role-missing", new UpdateRoleMetadataRequest("Operators", null), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateMetadata_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(updateThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);

        var result = await controller.UpdateMetadata("role-abc", new UpdateRoleMetadataRequest("Operators", null), CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task UpdateMetadata_WhenInvalid_Returns400()
    {
        var userManagement = new StubUserManagementService(updateThrows: new GovernanceValidationException(GovernanceValidationCodes.RoleDescriptionTooLong, "Role description must be 512 characters or fewer."));
        var controller = new RolesController(userManagement);

        var result = await controller.UpdateMetadata("role-abc", new UpdateRoleMetadataRequest("Operators", new string('x', 513)), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_WhenUnassigned_Returns204AndDoesNotRemoveAssignments()
    {
        var userManagement = new StubUserManagementService();
        var controller = new RolesController(userManagement);

        var result = await controller.Delete("role-abc", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        userManagement.DeleteCalled.Should().BeTrue();
        userManagement.RemoveRoleCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_WhenSystemRole_Returns409()
    {
        var userManagement = new StubUserManagementService(deleteThrows: new RoleLifecycleException(
            RoleLifecycleErrorCodes.SystemRole,
            "This built-in role cannot be changed or deleted."));
        var controller = new RolesController(userManagement);

        var result = await controller.Delete("role-platform-administrator", CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new
        {
            error = RoleLifecycleErrorCodes.SystemRole,
            message = "This built-in role cannot be changed or deleted.",
            assignmentCount = (int?)null
        });
        userManagement.RemoveRoleCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_WhenAssignmentsRemain_Returns409WithCount()
    {
        var userManagement = new StubUserManagementService(deleteThrows: new RoleLifecycleException(
            RoleLifecycleErrorCodes.AssignmentsRemain,
            "This role still has assignments and cannot be deleted.",
            assignmentCount: 3));
        var controller = new RolesController(userManagement);

        var result = await controller.Delete("role-abc", CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeEquivalentTo(new
        {
            error = RoleLifecycleErrorCodes.AssignmentsRemain,
            message = "This role still has assignments and cannot be deleted.",
            assignmentCount = 3
        });
        userManagement.RemoveRoleCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_WhenMissing_Returns404()
    {
        var userManagement = new StubUserManagementService(deleteThrows: new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, "role-missing"));
        var controller = new RolesController(userManagement);

        var result = await controller.Delete("role-missing", CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Delete_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(deleteThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);

        var result = await controller.Delete("role-abc", CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetAssignments_ReturnsHolderIdentityAndCount()
    {
        var userManagement = new StubUserManagementService(assignments: new RoleAssignmentList(
            5,
            [new RoleAssignmentHolder(new UserId("user-1"), "ada", "Ada")]));
        var controller = new RolesController(userManagement);

        var result = await controller.GetAssignments("role-abc", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<RoleAssignmentsDto>().Subject;
        body.Count.Should().Be(5);
        body.Holders.Should().ContainSingle(h => h.UserId == "user-1" && h.Username == "ada" && h.DisplayName == "Ada");
        typeof(RoleAssignmentHolderDto).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Should().BeEquivalentTo("UserId", "Username", "DisplayName");
    }

    [Fact]
    public async Task GetAssignments_WhenMissing_Returns404()
    {
        var userManagement = new StubUserManagementService(assignments: null, assignmentsConfigured: true);
        var controller = new RolesController(userManagement);

        var result = await controller.GetAssignments("role-missing", CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAssignments_WhenUnauthorized_Returns403()
    {
        var userManagement = new StubUserManagementService(assignmentsThrows: new UnauthorizedAccessException("deny"));
        var controller = new RolesController(userManagement);

        var result = await controller.GetAssignments("role-abc", CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Get_AssignmentCountComesFromCurrentAssignments()
    {
        var userManagement = new StubUserManagementService(
            role: SampleRole(),
            assignments: new RoleAssignmentList(4, [new RoleAssignmentHolder(new UserId("user-1"), "ada", "Ada")]));
        var controller = new RolesController(userManagement);

        var result = await controller.Get("role-abc", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var detail = ok.Value.Should().BeOfType<RoleDetailDto>().Subject;
        detail.AssignmentCount.Should().Be(4);
    }

    private static Role SampleRole(string name = "Operators", string? description = "Desk") =>
        new(
            new RoleId("role-abc"),
            name,
            description,
            [new PermissionDescriptor("user.read", new ResourceDescriptor("User", "instance"))],
            IsSystemRole: false);

    private sealed class StubUserManagementService : IUserManagementService
    {
        private readonly Exception? _setThrows;
        private readonly Exception? _getPermissionsThrows;
        private readonly Exception? _getRolesThrows;
        private readonly Exception? _getRoleThrows;
        private readonly Exception? _createThrows;
        private readonly Exception? _updateThrows;
        private readonly Exception? _deleteThrows;
        private readonly Exception? _assignmentsThrows;
        private readonly IReadOnlyList<Role> _roles;
        private readonly Role? _role;
        private readonly Role? _createdRole;
        private readonly Role? _updatedRole;
        private readonly RoleAssignmentList? _assignments;
        private readonly bool _assignmentsConfigured;
        private readonly Action<IReadOnlyList<PermissionDescriptor>>? _onSetPermissions;

        public StubUserManagementService(
            Exception? setThrows = null,
            Exception? getPermissionsThrows = null,
            Exception? getRolesThrows = null,
            Exception? getRoleThrows = null,
            IReadOnlyList<Role>? roles = null,
            Role? role = null,
            Action<IReadOnlyList<PermissionDescriptor>>? onSetPermissions = null,
            Exception? createThrows = null,
            Role? createdRole = null,
            Exception? updateThrows = null,
            Role? updatedRole = null,
            Exception? deleteThrows = null,
            Exception? assignmentsThrows = null,
            RoleAssignmentList? assignments = null,
            bool assignmentsConfigured = false)
        {
            _setThrows = setThrows;
            _getPermissionsThrows = getPermissionsThrows;
            _getRolesThrows = getRolesThrows;
            _getRoleThrows = getRoleThrows;
            _roles = roles ?? [];
            _role = role;
            _onSetPermissions = onSetPermissions;
            _createThrows = createThrows;
            _createdRole = createdRole;
            _updateThrows = updateThrows;
            _updatedRole = updatedRole;
            _deleteThrows = deleteThrows;
            _assignmentsThrows = assignmentsThrows;
            _assignments = assignments;
            _assignmentsConfigured = assignmentsConfigured || assignments is not null;
        }

        public string? CreatedName { get; private set; }

        public string? CreatedDescription { get; private set; }

        public IReadOnlyList<PermissionDescriptor>? CreatedPermissions { get; private set; }

        public string? UpdatedName { get; private set; }

        public string? UpdatedDescription { get; private set; }

        public bool DeleteCalled { get; private set; }

        public bool RemoveRoleCalled { get; private set; }

        public ValueTask<(IReadOnlyList<Apperture.Core.Domain.Identity.User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default, string? query = null) =>
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

        public ValueTask<Role> CreateRoleAsync(
            string name,
            string? description = null,
            IReadOnlyList<PermissionDescriptor>? permissions = null,
            CancellationToken cancellationToken = default)
        {
            if (_createThrows is not null)
                throw _createThrows;
            CreatedName = name;
            CreatedDescription = description;
            CreatedPermissions = permissions;
            return ValueTask.FromResult(_createdRole ?? throw new InvalidOperationException("stub create role missing"));
        }

        public ValueTask<Role> UpdateRoleMetadataAsync(
            RoleId roleId,
            string name,
            string? description,
            CancellationToken cancellationToken = default)
        {
            if (_updateThrows is not null)
                throw _updateThrows;
            UpdatedName = name;
            UpdatedDescription = description;
            return ValueTask.FromResult(_updatedRole ?? throw new InvalidOperationException("stub updated role missing"));
        }

        public ValueTask DeleteRoleAsync(RoleId roleId, CancellationToken cancellationToken = default)
        {
            if (_deleteThrows is not null)
                throw _deleteThrows;
            DeleteCalled = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<RoleAssignmentList?> GetRoleAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default)
        {
            if (_assignmentsThrows is not null)
                throw _assignmentsThrows;
            if (_assignmentsConfigured)
                return ValueTask.FromResult(_assignments);
            return ValueTask.FromResult<RoleAssignmentList?>(new RoleAssignmentList(0, []));
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

        public ValueTask AddIdentityLinkAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.IdentitySource source, string? externalId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveIdentityLinkAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask AssignRoleAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveRoleAsync(Apperture.Core.Domain.Identity.UserId userId, Apperture.Core.Domain.Identity.RoleId roleId, CancellationToken cancellationToken = default)
        {
            RemoveRoleCalled = true;
            throw new NotImplementedException();
        }

        public ValueTask SetLocalPasswordAsync(Apperture.Core.Domain.Identity.UserId userId, string newPassword, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SetLocalCredentialDisabledAsync(Apperture.Core.Domain.Identity.UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask<EmergencyAccessPosture> GetEmergencyAccessPostureAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
