using Apperture.Core.Application;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Options;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Auth administration service: permission checks, user/role/credential operations, and audit.
/// </summary>
public sealed class UserManagementService : IUserManagementService
{
    private const string AuditOutcome = "auth-admin";

    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IAuthorizationService _auth;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IResourceDescriptorRegistry _descriptorRegistry;
    private readonly ILocalCredentialRepository _localCredentialRepository;
    private readonly ILocalPasswordHasher _passwordHasher;
    private readonly IAuditService _audit;
    private readonly LocalAuthOptions _localOptions;

    public UserManagementService(
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IAuthorizationService auth,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IResourceDescriptorRegistry descriptorRegistry,
        ILocalCredentialRepository localCredentialRepository,
        ILocalPasswordHasher passwordHasher,
        IAuditService audit,
        IOptions<LocalAuthOptions> localOptions)
    {
        _principalContext = principalContext;
        _principalToUserResolver = principalToUserResolver;
        _auth = auth;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _descriptorRegistry = descriptorRegistry;
        _localCredentialRepository = localCredentialRepository;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _localOptions = localOptions.Value;
    }

    /// <inheritdoc />
    public async ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        return await _userRepository.GetPageAsync(skip, take, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<User?> GetUserByIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        return await _userRepository.GetByIdAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        return await _roleRepository.GetAllAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Role?> GetRoleAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        return await _roleRepository.GetAsync(roleId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PermissionDescriptor>> GetRolePermissionsAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        return role?.Permissions ?? Array.Empty<PermissionDescriptor>();
    }

    /// <inheritdoc />
    public async ValueTask SetRolePermissionsAsync(RoleId roleId, IReadOnlyList<PermissionDescriptor> permissions, CancellationToken cancellationToken = default)
    {
        var actorId = await RequirePermissionAssignAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("permission.assign", actorId, cancellationToken);

        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");

        if (permissions is { Count: > 0 })
        {
            for (var i = 0; i < permissions.Count; i++)
            {
                var p = permissions[i];
                try
                {
                    if (!_descriptorRegistry.IsPermissionRegistered(p))
                        throw new InvalidOperationException(
                            $"Permission at index {i} is not in the canonical governance catalog: Action={p.Action}, Resource={FormatResource(p.Resource)}. " +
                            "Only permissions registered at platform or module startup may be assigned to roles.");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("is not in the canonical") == false)
                {
                    throw new InvalidOperationException(
                        $"Permission at index {i} is malformed: {ex.Message}. Action={p.Action}, Resource={FormatResource(p.Resource)}.",
                        ex);
                }
            }
        }

        await _roleRepository.UpdatePermissionsAsync(roleId, permissions, cancellationToken);
        await AppendAuditAsync(GovernanceAuditActionNames.PermissionAssignmentUpdated, actorId, roleId.Value, $"roleId={roleId.Value} permissionCount={permissions.Count}", cancellationToken);
    }

    private static string FormatResource(ResourceDescriptor? r) =>
        r is null ? "null" : $"{r.ResourceType}:{r.ScopeKey}";

    /// <inheritdoc />
    public async ValueTask<UserId> CreateUserAsync(string displayName, IdentitySource source, string externalId, string? password = null, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("user.create", actorId, cancellationToken);
        ValidateEntraExternalId(source, externalId);
        var linkExternalId = source == IdentitySource.Local
            ? LocalAuthHelpers.NormalizeUsername(externalId)
            : externalId.Trim();

        var existing = await _userRepository.GetByIdentityLinkAsync(source, linkExternalId, cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException("An identity link with this source and external id already exists.");

        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            userId,
            displayName.Trim(),
            new[] { new IdentityLink(source, linkExternalId, LastAuthenticatedAt: null) },
            Array.Empty<RoleId>(),
            IsBreakGlassEligible: false);

        await _userRepository.AddAsync(user, cancellationToken);

        if (source == IdentitySource.Local && !string.IsNullOrEmpty(password))
        {
            var (hash, salt) = _passwordHasher.HashPassword(password);
            await _localCredentialRepository.SetPasswordAsync(userId, hash, salt, _passwordHasher.AlgorithmName, cancellationToken);
        }

        await AppendAuditAsync("user.created", actorId, userId.Value, $"userId={userId.Value}", cancellationToken);
        return userId;
    }

    /// <inheritdoc />
    public async ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("identity_link.add", actorId, cancellationToken);
        ValidateEntraExternalId(source, externalId);
        var linkExternalId = source == IdentitySource.Local
            ? LocalAuthHelpers.NormalizeUsername(externalId)
            : externalId.Trim();

        await _userRepository.AddIdentityLinkAsync(userId, source, linkExternalId, cancellationToken);
        await AppendAuditAsync("identity_link.added", actorId, userId.Value, $"userId={userId.Value} source={source} externalId={linkExternalId}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("identity_link.remove", actorId, cancellationToken);
        var linkExternalId = source == IdentitySource.Local
            ? LocalAuthHelpers.NormalizeUsername(externalId)
            : externalId.Trim();

        await _userRepository.RemoveIdentityLinkAsync(userId, source, linkExternalId, cancellationToken);
        await AppendAuditAsync("identity_link.removed", actorId, userId.Value, $"userId={userId.Value} source={source} externalId={linkExternalId}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireRoleAssignAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("role.assign", actorId, cancellationToken);
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User {userId.Value} not found.");
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");

        await _roleRepository.AssignToUserAsync(userId, roleId, cancellationToken);
        await AppendAuditAsync(GovernanceAuditActionNames.RoleAssigned, actorId, userId.Value, $"userId={userId.Value} roleId={roleId.Value}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireRoleAssignAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("role.remove", actorId, cancellationToken);
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User {userId.Value} not found.");
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");

        if (roleId.Value == BootstrapConstants.PlatformAdminRoleId)
        {
            var count = await _roleRepository.CountUserAssignmentsAsync(roleId, cancellationToken);
            if (count <= 1)
            {
                await AppendAuditAsync("auth_admin.blocked.last_platform_admin_removal", actorId, userId.Value, $"userId={userId.Value} roleId={roleId.Value}", cancellationToken);
                throw new InvalidOperationException("Cannot remove the last platform administrator role assignment.");
            }
        }

        await _roleRepository.RemoveFromUserAsync(userId, roleId, cancellationToken);
        await AppendAuditAsync(GovernanceAuditActionNames.RoleRemoved, actorId, userId.Value, $"userId={userId.Value} roleId={roleId.Value}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireCredentialManageAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("credential.password_set", actorId, cancellationToken);
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User {userId.Value} not found.");
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            throw new InvalidOperationException("User has no Local identity link; cannot set local password.");

        if (_localOptions.MinPasswordLength > 0 && (newPassword?.Length ?? 0) < _localOptions.MinPasswordLength)
            throw new InvalidOperationException($"Password must be at least {_localOptions.MinPasswordLength} characters.");

        var (hash, salt) = _passwordHasher.HashPassword(newPassword!);
        await _localCredentialRepository.SetPasswordAsync(userId, hash, salt, _passwordHasher.AlgorithmName, cancellationToken);
        await AppendAuditAsync("credential.password_set", actorId, userId.Value, $"userId={userId.Value}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireCredentialManageAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync(disabled ? "credential.disabled" : "credential.enabled", actorId, cancellationToken);
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User {userId.Value} not found.");
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            throw new InvalidOperationException("User has no Local identity link; cannot manage local credential.");

        var cred = await _localCredentialRepository.GetByUserIdAsync(userId, cancellationToken);
        if (cred is null)
            throw new InvalidOperationException("User has no local credential row; set a password first.");

        if (disabled)
        {
            var platformAdminRoleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
            var adminCount = await _roleRepository.CountUserAssignmentsAsync(platformAdminRoleId, cancellationToken);
            var userHasPlatformAdmin = user.AssignedRoleIds.Any(r => r.Value == BootstrapConstants.PlatformAdminRoleId);
            var onlyLocalLink = user.IdentityLinks.Count == 1 && user.IdentityLinks[0].Source == IdentitySource.Local;
            if (adminCount == 1 && userHasPlatformAdmin && onlyLocalLink)
            {
                await AppendAuditAsync("auth_admin.blocked.last_admin_credential_disabled", actorId, userId.Value, $"userId={userId.Value}", cancellationToken);
                throw new InvalidOperationException("Cannot disable the only local credential for the last platform administrator who has no other sign-in path.");
            }
        }

        await _localCredentialRepository.SetDisabledAsync(userId, disabled, cancellationToken);
        var action = disabled ? "credential.disabled" : "credential.enabled";
        await AppendAuditAsync(action, actorId, userId.Value, $"userId={userId.Value} disabled={disabled}", cancellationToken);
    }

    private async ValueTask RequireUserReadAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.UserRead, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to read users.");
    }

    private async ValueTask<ActorId> RequireUserManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.UserManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage users.");
        return new ActorId(actor.Id.Value);
    }

    private async ValueTask<ActorId> RequireRoleAssignAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.RoleAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to assign roles.");
        return new ActorId(actor.Id.Value);
    }

    private async ValueTask<ActorId> RequireCredentialManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.CredentialManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage credentials.");
        return new ActorId(actor.Id.Value);
    }

    private async ValueTask<ActorId> RequirePermissionAssignAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.PermissionAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to assign permissions.");
        return new ActorId(actor.Id.Value);
    }

    private static void ValidateEntraExternalId(IdentitySource source, string externalId)
    {
        if (source != IdentitySource.Entra)
            return;
        if (string.IsNullOrWhiteSpace(externalId))
            throw new InvalidOperationException("Entra external id must not be null or empty. Use the stable subject (oid or sub) from the token.");
        if (externalId.Contains('@'))
            throw new InvalidOperationException("Entra external id must be the stable subject (oid or sub), not an email or UPN.");
    }

    /// <summary>
    /// If the current session is break-glass, appends a durable audit record for the auth-admin operation (visibility only; no secrets).
    /// </summary>
    private async ValueTask AppendBreakGlassUseAuditIfNeededAsync(string operation, ActorId actorId, CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is not HumanActor human || !human.IsBreakGlassSession)
            return;
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            GovernanceAuditActionNames.BreakGlassAdminOperation,
            new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
            "auth",
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            $"operation={operation}");
        await _audit.AppendAsync(record, cancellationToken);
    }

    private async ValueTask AppendAuditAsync(string action, ActorId actorId, string? resourceId, string? details, CancellationToken cancellationToken)
    {
        var resource = string.IsNullOrEmpty(resourceId)
            ? null
            : new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance);
        var record = new AuditRecord(
            new AuditRecordId(Guid.NewGuid().ToString("N")),
            actorId,
            action,
            resource,
            AuditOutcome,
            DateTimeOffset.UtcNow,
            new CorrelationId(null),
            details);
        await _audit.AppendAsync(record, cancellationToken);
    }
}
