using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UsersControllerIdentityLinkTests
{
    private const string EntraOid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string TargetUserId = "usridentitylinktest0001";

    [Fact]
    public async Task AddIdentityLink_success_returns_no_content()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", EntraOid),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        service.AddCalls.Should().ContainSingle(c =>
            c.UserId.Value == TargetUserId && c.Source == IdentitySource.Entra && c.ExternalId == EntraOid);
    }

    [Fact]
    public async Task AddIdentityLink_duplicate_same_user_is_idempotent_no_content()
    {
        var service = new StubUserManagement(duplicateSameUser: true);
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", EntraOid),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        service.AddCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AddIdentityLink_external_id_in_use_returns_conflict_with_code()
    {
        var service = new StubUserManagement(throwOnAdd: new IdentityLinkException(
            IdentityLinkErrorCodes.ExternalIdInUse,
            "already attached to another user"));
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", EntraOid),
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var body = conflict.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be(IdentityLinkErrorCodes.ExternalIdInUse);
    }

    [Fact]
    public async Task AddIdentityLink_user_not_found_returns_not_found_with_code()
    {
        var service = new StubUserManagement(throwOnAdd: new IdentityLinkException(
            IdentityLinkErrorCodes.UserNotFound,
            "User missing"));
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            "missing-user",
            new AddIdentityLinkRequest("Entra", EntraOid),
            CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = notFound.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be(IdentityLinkErrorCodes.UserNotFound);
    }

    [Fact]
    public async Task AddIdentityLink_without_permission_returns_forbidden()
    {
        var service = new StubUserManagement(throwOnAdd: new UnauthorizedAccessException());
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", EntraOid),
            CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AddIdentityLink_invalid_entra_external_id_returns_bad_request_with_code()
    {
        var service = new StubUserManagement(throwOnAdd: new IdentityLinkException(
            IdentityLinkErrorCodes.InvalidEntraExternalId,
            "must be oid"));
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", "user@contoso.com"),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = bad.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be(IdentityLinkErrorCodes.InvalidEntraExternalId);
    }

    [Fact]
    public async Task AddIdentityLink_missing_external_id_returns_bad_request_with_code()
    {
        var controller = NewController(new StubUserManagement());

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", "  "),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = bad.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be(IdentityLinkErrorCodes.MissingExternalId);
    }

    [Fact]
    public async Task SetLocalCredentialDisabled_without_recent_strong_authentication_returns_forbidden_with_code()
    {
        var service = new StubUserManagement(throwOnDisable: new StrongAuthenticationException(
            StrongAuthenticationErrorCodes.Required,
            "step-up required"));
        var controller = NewController(service);

        var result = await controller.SetLocalCredentialDisabled(
            TargetUserId,
            new SetLocalCredentialDisabledRequest(false),
            CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
        var body = objectResult.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be(StrongAuthenticationErrorCodes.Required);
    }

    [Fact]
    public async Task SetLocalCredentialDisabled_when_strong_authentication_is_not_configured_returns_conflict()
    {
        var service = new StubUserManagement(throwOnDisable: new StrongAuthenticationException(
            StrongAuthenticationErrorCodes.NotConfigured,
            "not configured"));
        var controller = NewController(service);

        var result = await controller.SetLocalCredentialDisabled(
            TargetUserId,
            new SetLocalCredentialDisabledRequest(false),
            CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(409);
        var body = objectResult.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be(StrongAuthenticationErrorCodes.NotConfigured);
    }

    private static UsersController NewController(StubUserManagement service) =>
        new(service, new StubLocalCredentialRepository(), new StubRoleProvider([]));

    private sealed class StubUserManagement : IUserManagementService
    {
        private readonly Exception? _throwOnAdd;
        private readonly Exception? _throwOnDisable;
        private readonly bool _duplicateSameUser;

        public StubUserManagement(Exception? throwOnAdd = null, bool duplicateSameUser = false, Exception? throwOnDisable = null)
        {
            _throwOnAdd = throwOnAdd;
            _duplicateSameUser = duplicateSameUser;
            _throwOnDisable = throwOnDisable;
        }

        public List<(UserId UserId, IdentitySource Source, string ExternalId)> AddCalls { get; } = [];

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
        {
            if (_throwOnAdd is not null)
                throw _throwOnAdd;
            if (_duplicateSameUser)
                return ValueTask.CompletedTask;
            AddCalls.Add((userId, source, externalId));
            return ValueTask.CompletedTask;
        }

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(((IReadOnlyList<User>)([]), 0));

        public ValueTask<User?> GetUserByIdAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<User?>(null);

        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<PermissionDescriptor>> GetRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PermissionDescriptor>>([]);

        public ValueTask SetRolePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<UserId> CreateUserAsync(string displayName, IdentitySource source, string externalId, string? password = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
        {
            if (_throwOnDisable is not null)
                throw _throwOnDisable;
            return ValueTask.CompletedTask;
        }
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

    private sealed class StubRoleProvider : IRoleProvider
    {
        public StubRoleProvider(IReadOnlyList<Role> roles) => _roles = roles;
        private readonly IReadOnlyList<Role> _roles;

        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_roles);

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_roles);

        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
