using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Apperture.Management.Api.Controllers;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class UsersControllerCreateUserTests
{
    [Fact]
    public async Task Create_local_user_returns_created()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "operator", "Password1!"),
            CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var body = created.Value.Should().BeOfType<CreateUserResponse>().Subject;
        body.UserId.Should().Be("usr-created");
        service.CreateCalls.Should().ContainSingle(c =>
            c.DisplayName == "Operator" && c.Source == IdentitySource.Local && c.ExternalId == "operator" && c.Password == "Password1!");
    }

    [Fact]
    public async Task Create_duplicate_returns_conflict()
    {
        var service = new StubUserManagement(throwOnCreate: new InvalidOperationException("An identity link with this source and external id already exists."));
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "operator", "Password1!"),
            CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Create_without_permission_returns_forbidden()
    {
        var service = new StubUserManagement(throwOnCreate: new UnauthorizedAccessException());
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "operator", "Password1!"),
            CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Create_reserved_username_returns_conflict()
    {
        var service = new StubUserManagement(throwOnCreate: new EmergencyAccessException(
            EmergencyAccessErrorCodes.ReservedUsername,
            "That username is reserved for Emergency Access."));
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "breakglass", "Password1!"),
            CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Create_short_password_returns_bad_request()
    {
        var service = new StubUserManagement(throwOnCreate: new InvalidOperationException("Password must be at least 8 characters."));
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "operator", "short"),
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_missing_display_name_returns_bad_request_without_calling_service()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("  ", "Local", "operator", "Password1!"),
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        service.CreateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_local_user_accepts_username_without_external_id()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", Password: "Password1!", Username: "operator"),
            CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        service.CreateCalls.Should().ContainSingle(c =>
            c.Source == IdentitySource.Local && c.ExternalId == "operator");
    }

    [Fact]
    public async Task Create_local_user_accepts_matching_username_and_external_id_after_normalization()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "  Operator ", "Password1!", Username: "OPERATOR"),
            CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        // The preferred field's value goes to the service, which owns normalization.
        service.CreateCalls.Should().ContainSingle(c => c.ExternalId == "OPERATOR");
    }

    [Fact]
    public async Task Create_local_user_with_different_username_and_external_id_returns_username_mismatch()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "operator.one", "Password1!", Username: "operator.two"),
            CancellationToken.None);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        System.Text.Json.JsonSerializer.Serialize(bad.Value).Should().Contain("\"code\":\"username_mismatch\"");
        service.CreateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_local_user_without_username_or_external_id_returns_bad_request()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.Create(
            new CreateUserRequest("Operator", "Local", "  ", "Password1!", Username: " "),
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        service.CreateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_entra_user_is_unchanged_and_ignores_username()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var withoutExternalId = await controller.Create(
            new CreateUserRequest("Operator", "Entra", Username: "operator"),
            CancellationToken.None);
        withoutExternalId.Result.Should().BeOfType<BadRequestObjectResult>();
        service.CreateCalls.Should().BeEmpty();

        await controller.Create(
            new CreateUserRequest("Operator", "Entra", " 11111111-2222-3333-4444-555555555555 ", Username: "operator"),
            CancellationToken.None);
        service.CreateCalls.Should().ContainSingle(c =>
            c.Source == IdentitySource.Entra && c.ExternalId == "11111111-2222-3333-4444-555555555555");
    }

    private static UsersController NewController(StubUserManagement service) =>
        new(service, new StubLocalCredentialRepository(), new StubRoleProvider());

    private sealed class StubUserManagement : IUserManagementService
    {
        private readonly Exception? _throwOnCreate;

        public StubUserManagement(Exception? throwOnCreate = null) => _throwOnCreate = throwOnCreate;

        public List<(string DisplayName, IdentitySource Source, string ExternalId, string? Password)> CreateCalls { get; } = [];

        public ValueTask<UserId> CreateUserAsync(
            string displayName,
            IdentitySource source,
            string externalId,
            string? password = null,
            CancellationToken cancellationToken = default,
            string? contactEmail = null,
            bool sendSetupInvitation = false)
        {
            if (_throwOnCreate is not null)
                throw _throwOnCreate;
            CreateCalls.Add((displayName, source, externalId, password));
            return ValueTask.FromResult(new UserId("usr-created"));
        }

        public ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default, string? query = null) =>
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

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string? externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<EmergencyAccessPosture> GetEmergencyAccessPostureAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EmergencyAccessPosture(false, true, null, null, false, false, false, 0));
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
        public ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Role?>(null);

        public ValueTask<IReadOnlyList<Role>> GetRolesForUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<Role>>([]);

        public ValueTask AssignRoleToUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveRoleFromUserAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
