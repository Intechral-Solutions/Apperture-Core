using Apperture.ControlPlane.Security;
using Apperture.Core.Application.AdministratorViability;
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
    public async Task AddIdentityLink_recovery_access_returns_conflict()
    {
        var service = new StubUserManagement(throwOnAdd: new EmergencyAccessException(
            EmergencyAccessErrorCodes.UnsafeOperation,
            "Recovery Access is system-managed."));
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", EntraOid),
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var body = conflict.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be(EmergencyAccessErrorCodes.UnsafeOperation);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddIdentityLink_local_without_external_id_defers_to_the_users_username(string? omitted)
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Local", omitted),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        service.AddCalls.Should().ContainSingle(c =>
            c.UserId.Value == TargetUserId && c.Source == IdentitySource.Local && c.ExternalId == null);
    }

    [Fact]
    public async Task AddIdentityLink_local_name_that_differs_from_the_username_returns_bad_request_username_mismatch()
    {
        var service = new StubUserManagement(throwOnAdd: new UsernameException(
            UsernameErrorCodes.Mismatch,
            "A local sign-in must use this user's existing username."));
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Local", "someone.else"),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var body = bad.Value.Should().BeOfType<IdentityLinkErrorResponse>().Subject;
        body.Code.Should().Be("username_mismatch");
    }

    [Fact]
    public async Task AddIdentityLink_entra_without_external_id_returns_bad_request_without_calling_service()
    {
        var service = new StubUserManagement();
        var controller = NewController(service);

        var result = await controller.AddIdentityLink(
            TargetUserId,
            new AddIdentityLinkRequest("Entra", null),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeOfType<IdentityLinkErrorResponse>().Which.Code.Should().Be(IdentityLinkErrorCodes.MissingExternalId);
        service.AddCalls.Should().BeEmpty();
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

    // ---- IAM-10.2b: the viability refusals are typed 409s, for a Local and an Entra link alike, never an unhandled exception ----

    public static IEnumerable<object[]> ViabilityRefusals()
    {
        yield return new object[] { new GovernanceConflictException(GovernanceConflictCodes.LastViableSignInMethod, "m"), "last_viable_sign_in_method", false };
        yield return new object[] { new GovernanceConflictException(GovernanceConflictCodes.EntraIndependentFallbackRequired, "m"), "entra_independent_fallback_required", false };
        yield return new object[] { new GovernanceConflictException(GovernanceConflictCodes.AdministratorViabilityUnavailable, "m"), "administrator_viability_unavailable", false };
        yield return new object[] { new PlatformAdministratorSetBusyException(PlatformAdministratorSetLockOutcome.TimedOut), "governance_operation_busy", true };
    }

    [Theory]
    [MemberData(nameof(ViabilityRefusals))]
    public async Task RemoveIdentityLink_maps_the_viability_refusals_to_409_for_both_sources(Exception thrown, string code, bool retry)
    {
        foreach (var source in new[] { "Local", "Entra" })
        {
            var controller = NewController(new StubUserManagement(throwOnRemoveLink: thrown));

            var result = await controller.RemoveIdentityLink(TargetUserId, source, source == "Local" ? "operator" : EntraOid, CancellationToken.None);

            var conflict = result.Should().BeOfType<ConflictObjectResult>($"{source} link removal").Subject;
            var json = System.Text.Json.JsonSerializer.SerializeToElement(conflict.Value);
            json.GetProperty("code").GetString().Should().Be(code);
            json.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
            json.TryGetProperty("retry", out var retryProperty).Should().Be(retry);
            if (retry)
                retryProperty.GetBoolean().Should().BeTrue();
        }
    }

    [Theory]
    [MemberData(nameof(ViabilityRefusals))]
    public async Task SetLocalCredentialDisabled_maps_the_viability_refusals_to_409(Exception thrown, string code, bool retry)
    {
        var controller = NewController(new StubUserManagement(throwOnDisable: thrown));

        var result = await controller.SetLocalCredentialDisabled(TargetUserId, new SetLocalCredentialDisabledRequest(true), CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.SerializeToElement(conflict.Value);
        json.GetProperty("code").GetString().Should().Be(code);
        json.TryGetProperty("retry", out _).Should().Be(retry);
    }

    [Fact]
    public async Task An_internal_audit_failure_is_not_mapped_to_a_client_error_by_the_link_and_credential_endpoints()
    {
        var removal = NewController(new StubUserManagement(throwOnRemoveLink: new Apperture.Core.Application.Audit.AuditInvariantException("defect")));
        var disable = NewController(new StubUserManagement(throwOnDisable: new Apperture.Core.Application.Audit.AuditInvariantException("defect")));

        var removeLink = () => removal.RemoveIdentityLink(TargetUserId, "Local", "operator", CancellationToken.None);
        var disableCredential = () => disable.SetLocalCredentialDisabled(TargetUserId, new SetLocalCredentialDisabledRequest(true), CancellationToken.None);

        await removeLink.Should().ThrowAsync<Apperture.Core.Application.Audit.AuditInvariantException>("it reaches the host's handler as the internal error it is");
        await disableCredential.Should().ThrowAsync<Apperture.Core.Application.Audit.AuditInvariantException>();
    }

    // IAM-10.2b WP5 (pre-hand-over review C4): setting a password became a governance endpoint with SI-1, and its catch of
    // InvalidOperationException mapped an audit invariant failure to 400, or to 404 by message. It now lets it through as the credential
    // endpoint does, while an ordinary not-found and an invalid request keep their 404 and 400.
    [Fact]
    public async Task An_internal_audit_failure_is_not_mapped_to_a_client_error_by_the_password_endpoint()
    {
        var request = new SetPasswordRequest("quiet-maple-lantern-harbor");
        var defect = NewController(new StubUserManagement { ThrowOnSetPassword = new Apperture.Core.Application.Audit.AuditInvariantException("the audit row was not found") });
        var missing = NewController(new StubUserManagement { ThrowOnSetPassword = new InvalidOperationException("User not found.") });
        var invalid = NewController(new StubUserManagement { ThrowOnSetPassword = new InvalidOperationException("The password is too short.") });

        var setWithDefect = () => defect.SetLocalPassword(TargetUserId, request, CancellationToken.None);

        await setWithDefect.Should().ThrowAsync<Apperture.Core.Application.Audit.AuditInvariantException>(
            "it reaches the host's handler as the internal error it is, even when its message says 'not found'");
        (await missing.SetLocalPassword(TargetUserId, request, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
        (await invalid.SetLocalPassword(TargetUserId, request, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task A_gate_failure_that_is_not_contention_is_not_presented_as_a_retry()
    {
        var controller = NewController(new StubUserManagement(throwOnRemoveLink: new PlatformAdministratorSetGateException(-999)));

        var act = () => controller.RemoveIdentityLink(TargetUserId, "Local", "operator", CancellationToken.None);

        await act.Should().ThrowAsync<PlatformAdministratorSetGateException>("an unexpected lock result is an internal failure, not a 409 with a retry hint");
    }

    private static UsersController NewController(StubUserManagement service) =>
        new(service, new StubLocalCredentialRepository(), new StubRoleProvider([]));

    private sealed class StubUserManagement : IUserManagementService
    {
        private readonly Exception? _throwOnAdd;
        private readonly Exception? _throwOnDisable;
        private readonly Exception? _throwOnRemoveLink;
        private readonly bool _duplicateSameUser;

        public StubUserManagement(Exception? throwOnAdd = null, bool duplicateSameUser = false, Exception? throwOnDisable = null, Exception? throwOnRemoveLink = null)
        {
            _throwOnAdd = throwOnAdd;
            _duplicateSameUser = duplicateSameUser;
            _throwOnDisable = throwOnDisable;
            _throwOnRemoveLink = throwOnRemoveLink;
        }

        public List<(UserId UserId, IdentitySource Source, string? ExternalId)> AddCalls { get; } = [];

        public ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string? externalId, CancellationToken cancellationToken = default)
        {
            if (_throwOnAdd is not null)
                throw _throwOnAdd;
            if (_duplicateSameUser)
                return ValueTask.CompletedTask;
            AddCalls.Add((userId, source, externalId));
            return ValueTask.CompletedTask;
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

        public ValueTask<UserId> CreateUserAsync(
            string displayName,
            IdentitySource source,
            string externalId,
            string? password = null,
            CancellationToken cancellationToken = default,
            string? contactEmail = null,
            bool sendSetupInvitation = false) =>
            throw new NotImplementedException();

        public ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default) =>
            throw (_throwOnRemoveLink ?? new NotImplementedException());

        public ValueTask AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        /// <summary>What setting a password throws (IAM-10.2b WP5); nothing set means the call was not expected.</summary>
        public Exception? ThrowOnSetPassword { get; init; }

        public ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default) =>
            throw ThrowOnSetPassword ?? new NotImplementedException();

        public ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
        {
            if (_throwOnDisable is not null)
                throw _throwOnDisable;
            return ValueTask.CompletedTask;
        }

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
