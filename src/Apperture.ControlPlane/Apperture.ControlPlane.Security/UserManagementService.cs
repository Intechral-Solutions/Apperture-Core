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
    private readonly IAuthorizationService _auth;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILocalCredentialRepository _localCredentialRepository;
    private readonly ILocalPasswordHasher _passwordHasher;
    private readonly IAuditService _audit;
    private readonly LocalAuthOptions _localOptions;

    public UserManagementService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILocalCredentialRepository localCredentialRepository,
        ILocalPasswordHasher passwordHasher,
        IAuditService audit,
        IOptions<LocalAuthOptions> localOptions)
    {
        _principalContext = principalContext;
        _auth = auth;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
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
    public async ValueTask<UserId> CreateUserAsync(string displayName, IdentitySource source, string externalId, string? password = null, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
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
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User {userId.Value} not found.");
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");

        await _roleRepository.AssignToUserAsync(userId, roleId, cancellationToken);
        await AppendAuditAsync("role.assigned", actorId, userId.Value, $"userId={userId.Value} roleId={roleId.Value}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireRoleAssignAsync(cancellationToken);
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User {userId.Value} not found.");
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");

        await _roleRepository.RemoveFromUserAsync(userId, roleId, cancellationToken);
        await AppendAuditAsync("role.removed", actorId, userId.Value, $"userId={userId.Value} roleId={roleId.Value}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireCredentialManageAsync(cancellationToken);
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
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User {userId.Value} not found.");
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            throw new InvalidOperationException("User has no Local identity link; cannot manage local credential.");

        var cred = await _localCredentialRepository.GetByUserIdAsync(userId, cancellationToken);
        if (cred is null)
            throw new InvalidOperationException("User has no local credential row; set a password first.");

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
