using Apperture.Core.Application;
using Apperture.Core.Application.Accounts;
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
    private readonly IEntraIdentityAssociationService? _entraAssociation;
    private readonly IOptionsMonitor<EntraAuthOptions>? _entraOptions;
    private readonly IStrongAuthenticationGate? _strongAuthentication;
    private readonly IEmergencyAccessService? _emergencyAccess;
    private readonly IAccountInvitationService? _invitations;
    private readonly IAccountActionTokenService? _actionTokens;

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
        IOptions<LocalAuthOptions> localOptions,
        IEntraIdentityAssociationService? entraAssociation = null,
        IOptionsMonitor<EntraAuthOptions>? entraOptions = null,
        IStrongAuthenticationGate? strongAuthentication = null,
        IEmergencyAccessService? emergencyAccess = null,
        IAccountInvitationService? invitations = null,
        IAccountActionTokenService? actionTokens = null)
    {
        _entraAssociation = entraAssociation;
        _entraOptions = entraOptions;
        _strongAuthentication = strongAuthentication;
        _emergencyAccess = emergencyAccess;
        _invitations = invitations;
        _actionTokens = actionTokens;
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
    public async ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetUserPageAsync(int skip, int take, CancellationToken cancellationToken = default, string? query = null)
    {
        await RequireUserReadAsync(cancellationToken);
        // Reads are not audited here, and the search text must never reach an audit record.
        return await _userRepository.SearchPageAsync(UserSearchCriteria.Create(query, skip, take), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<User?> GetUserByIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is not null && EmergencyAccessIdentity.IsSystemManaged(user))
            return null;
        return user;
    }

    /// <inheritdoc />
    public async ValueTask<EmergencyAccessPosture> GetEmergencyAccessPostureAsync(CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        if (_emergencyAccess is null)
        {
            return new EmergencyAccessPosture(
                IsHealthy: false,
                FallbackSafeguardRequired: true,
                DesignatedUserId: null,
                DesignatedDisplayName: null,
                DesignatedIsPlatformAdministrator: false,
                LocalCredentialEnabled: false,
                NativeMfaEnrolled: false,
                UnusedRecoveryCodeCount: 0);
        }

        return await _emergencyAccess.GetPostureAsync(cancellationToken);
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

        if (role.IsSystemRole)
            throw await RefuseSystemRoleMutationAsync(actorId, roleId, cancellationToken);

        ValidateCatalogPermissions(permissions);

        await _roleRepository.UpdatePermissionsAsync(roleId, permissions, cancellationToken);
        await AppendAuditAsync(GovernanceAuditActionNames.PermissionAssignmentUpdated, actorId, roleId.Value, $"roleId={roleId.Value} permissionCount={permissions.Count}", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Role> CreateRoleAsync(
        string name,
        string? description = null,
        IReadOnlyList<PermissionDescriptor>? permissions = null,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequireRoleManageAsync(cancellationToken);
        var grants = permissions ?? Array.Empty<PermissionDescriptor>();
        if (grants.Count > 0)
            await RequirePermissionAssignAsync(cancellationToken);

        var normalizedName = NormalizeRoleName(name);
        var normalizedDescription = NormalizeRoleDescription(description);
        ValidateCatalogPermissions(grants);
        await EnsureRoleNameAvailableAsync(normalizedName, excludeRoleId: null, cancellationToken);

        var role = new Role(
            NewUserCreatedRoleId(),
            normalizedName,
            normalizedDescription,
            grants,
            IsSystemRole: false);

        try
        {
            await _roleRepository.CreateAsync(role, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsDuplicateRoleName(ex))
        {
            throw new RoleLifecycleException(RoleLifecycleErrorCodes.DuplicateName, "A role with this name already exists.");
        }

        await AppendAuditAsync(
            GovernanceAuditActionNames.RoleCreated,
            actorId,
            role.Id.Value,
            $"roleId={role.Id.Value} name={role.Name} permissionCount={grants.Count}",
            cancellationToken);
        return role;
    }

    /// <inheritdoc />
    public async ValueTask<Role> UpdateRoleMetadataAsync(
        RoleId roleId,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequireRoleManageAsync(cancellationToken);
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");
        if (role.IsSystemRole)
            throw await RefuseSystemRoleMutationAsync(actorId, roleId, cancellationToken);

        var normalizedName = NormalizeRoleName(name);
        var normalizedDescription = NormalizeRoleDescription(description);
        await EnsureRoleNameAvailableAsync(normalizedName, roleId, cancellationToken);

        try
        {
            await _roleRepository.UpdateMetadataAsync(roleId, normalizedName, normalizedDescription, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsDuplicateRoleName(ex))
        {
            throw new RoleLifecycleException(RoleLifecycleErrorCodes.DuplicateName, "A role with this name already exists.");
        }

        var updated = role with { Name = normalizedName, Description = normalizedDescription };
        await AppendAuditAsync(
            GovernanceAuditActionNames.RoleMetadataUpdated,
            actorId,
            roleId.Value,
            $"roleId={roleId.Value} name={normalizedName}",
            cancellationToken);
        return updated;
    }

    /// <inheritdoc />
    public async ValueTask DeleteRoleAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireRoleManageAsync(cancellationToken);
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");
        if (role.IsSystemRole)
            throw await RefuseSystemRoleMutationAsync(actorId, roleId, cancellationToken);

        var result = await _roleRepository.TryDeleteUnassignedAsync(roleId, cancellationToken);
        switch (result.Outcome)
        {
            case RoleUnassignedDeleteOutcome.NotFound:
                throw new InvalidOperationException($"Role {roleId.Value} not found.");
            case RoleUnassignedDeleteOutcome.AssignmentsRemain:
                await AppendAuditAsync(
                    GovernanceAuditActionNames.RoleMutationRejected,
                    actorId,
                    roleId.Value,
                    $"roleId={roleId.Value} reason=assignments_remain assignmentCount={result.AssignmentCount}",
                    cancellationToken);
                throw new RoleLifecycleException(
                    RoleLifecycleErrorCodes.AssignmentsRemain,
                    "This role still has assignments and cannot be deleted.",
                    result.AssignmentCount);
            case RoleUnassignedDeleteOutcome.Deleted:
                await AppendAuditAsync(
                    GovernanceAuditActionNames.RoleDeleted,
                    actorId,
                    roleId.Value,
                    $"roleId={roleId.Value} name={result.Name ?? role.Name}",
                    cancellationToken);
                return;
            default:
                throw new InvalidOperationException($"Unexpected role delete outcome {result.Outcome}.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<RoleAssignmentList?> GetRoleAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        await RequireUserReadAsync(cancellationToken);
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            return null;

        var userIds = await _roleRepository.ListUserIdsWithRoleAsync(roleId, cancellationToken);
        var holders = new List<RoleAssignmentHolder>(userIds.Count);
        foreach (var userId in userIds)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            holders.Add(new RoleAssignmentHolder(userId, user?.Username ?? userId.Value, user?.DisplayName ?? userId.Value));
        }

        return new RoleAssignmentList(holders.Count, holders);
    }

    private static string FormatResource(ResourceDescriptor? r) =>
        r is null ? "null" : $"{r.ResourceType}:{r.ScopeKey}";

    private void ValidateCatalogPermissions(IReadOnlyList<PermissionDescriptor> permissions)
    {
        if (permissions is not { Count: > 0 })
            return;

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

    private static string NormalizeRoleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Role name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > 256)
            throw new InvalidOperationException("Role name must be 256 characters or fewer.");
        return trimmed;
    }

    private static string? NormalizeRoleDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        var trimmed = description.Trim();
        if (trimmed.Length > 512)
            throw new InvalidOperationException("Role description must be 512 characters or fewer.");
        return trimmed;
    }

    private async ValueTask EnsureRoleNameAvailableAsync(string name, RoleId? excludeRoleId, CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.GetAllAsync(cancellationToken);
        if (roles.Any(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)
                && (excludeRoleId is null || r.Id.Value != excludeRoleId.Value)))
        {
            throw new RoleLifecycleException(RoleLifecycleErrorCodes.DuplicateName, "A role with this name already exists.");
        }
    }

    private static RoleId NewUserCreatedRoleId()
    {
        RoleId id;
        do
        {
            id = new RoleId("role-" + Guid.NewGuid().ToString("N"));
        }
        while (string.Equals(id.Value, BootstrapConstants.PlatformAdminRoleId, StringComparison.Ordinal));
        return id;
    }

    private static bool IsDuplicateRoleName(InvalidOperationException ex) =>
        ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    private async ValueTask<RoleLifecycleException> RefuseSystemRoleMutationAsync(
        ActorId actorId,
        RoleId roleId,
        CancellationToken cancellationToken)
    {
        await AppendAuditAsync(
            GovernanceAuditActionNames.RoleMutationRejected,
            actorId,
            roleId.Value,
            $"roleId={roleId.Value} reason=system_role",
            cancellationToken);
        return new RoleLifecycleException(
            RoleLifecycleErrorCodes.SystemRole,
            "This built-in role cannot be changed or deleted.");
    }

    /// <inheritdoc />
    public async ValueTask<UserId> CreateUserAsync(
        string displayName,
        IdentitySource source,
        string externalId,
        string? password = null,
        CancellationToken cancellationToken = default,
        string? contactEmail = null,
        bool sendSetupInvitation = false)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("user.create", actorId, cancellationToken);
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("Display name is required.");
        ValidateEntraExternalId(source, externalId);
        var linkExternalId = source == IdentitySource.Local
            ? LocalAuthHelpers.NormalizeUsername(externalId)
            : externalId.Trim();
        if (string.IsNullOrWhiteSpace(linkExternalId))
            throw new InvalidOperationException("A username is required.");

        if (source == IdentitySource.Local)
        {
            if (sendSetupInvitation && _invitations is null)
                throw new InvalidOperationException("Apperture setup invitations are not available.");
            if (EmergencyAccessIdentity.IsReservedLocalUsername(linkExternalId))
            {
                throw new EmergencyAccessException(
                    EmergencyAccessErrorCodes.ReservedUsername,
                    "That username is reserved for Emergency Access.");
            }
            // A new Local user's sign-in name is a new username assignment.
            UsernamePolicy.RequireNewAssignment(linkExternalId);
            if (sendSetupInvitation)
            {
                if (!string.IsNullOrEmpty(password))
                    throw new InvalidOperationException("Do not supply a password when sending a setup invitation.");
                if (!ContactEmailAddress.IsValid(contactEmail))
                    throw new AccountSecurityException(AccountSecurityErrorCodes.InvalidEmail, "A contact email is required for the setup invitation.");
            }
            else
            {
                if (string.IsNullOrEmpty(password))
                    throw new InvalidOperationException("A password is required when creating a local user without a setup invitation.");
                LocalPasswordPolicy.Require(
                    password,
                    LocalPasswordPolicy.ForLocalIdentity(linkExternalId, displayName, contactEmail));
            }
        }

        if (source == IdentitySource.Entra)
        {
            throw new IdentityLinkException(
                IdentityLinkErrorCodes.InvalidEntraExternalId,
                "Microsoft identities are added from the directory or by linking, not by creating a user with an object id alone.");
        }

        var existing = await _userRepository.GetByIdentityLinkAsync(source, linkExternalId, cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException("An identity link with this source and external id already exists.");

        var userId = new UserId(Guid.NewGuid().ToString("N")[..24]);
        var user = new User(
            userId,
            displayName.Trim(),
            new[] { new IdentityLink(source, linkExternalId, LastAuthenticatedAt: null) },
            Array.Empty<RoleId>(),
            IsBreakGlassEligible: false)
        {
            Username = linkExternalId
        };

        await _userRepository.AddAsync(user, cancellationToken);

        if (source == IdentitySource.Local && !string.IsNullOrEmpty(password))
        {
            var (hash, salt) = _passwordHasher.HashPassword(password);
            await _localCredentialRepository.SetPasswordAsync(userId, hash, salt, _passwordHasher.AlgorithmName, cancellationToken);
        }

        await AppendAuditAsync("user.created", actorId, userId.Value, $"userId={userId.Value}", cancellationToken);

        if (sendSetupInvitation && source == IdentitySource.Local)
            await SendSetupInvitationCoreAsync(userId, contactEmail, cancellationToken);

        return userId;
    }

    /// <inheritdoc />
    public async ValueTask<AccountInvitationResult> SendSetupInvitationAsync(
        UserId userId,
        string? contactEmail = null,
        CancellationToken cancellationToken = default)
    {
        await RequireUserManageAsync(cancellationToken);
        return await SendSetupInvitationCoreAsync(userId, contactEmail, cancellationToken);
    }

    private async ValueTask<AccountInvitationResult> SendSetupInvitationCoreAsync(
        UserId userId,
        string? contactEmail,
        CancellationToken cancellationToken)
    {
        if (_invitations is null)
            throw new InvalidOperationException("Apperture setup invitations are not available.");

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId.Value} not found.");
        var email = contactEmail ?? user.PendingEmailAddress ?? user.EmailAddress;
        return await _invitations.SendSetupInvitationAsync(userId, email ?? "", cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string? externalId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        await AppendBreakGlassUseAuditIfNeededAsync("identity_link.add", actorId, cancellationToken);
        ValidateEntraExternalId(source, externalId);

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new IdentityLinkException(IdentityLinkErrorCodes.UserNotFound, $"User {userId.Value} not found.");

        if (EmergencyAccessIdentity.IsSystemManaged(user))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.UnsafeOperation,
                "Recovery Access is system-managed. Identity links cannot be changed through ordinary user administration.");
        }

        // A Local link's name is the user's username, so it may be omitted and then defaults to it.
        var linkExternalId = source == IdentitySource.Local
            ? string.IsNullOrWhiteSpace(externalId) ? user.Username : LocalAuthHelpers.NormalizeUsername(externalId)
            : externalId!.Trim();

        if (source == IdentitySource.Local && EmergencyAccessIdentity.IsReservedLocalUsername(linkExternalId))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.ReservedUsername,
                "That username is reserved for Emergency Access.");
        }

        if (user.IdentityLinks.Any(l => l.Source == source && l.ExternalId == linkExternalId))
            return;

        if (source == IdentitySource.Entra && _entraAssociation is not null)
        {
            if (user.IdentityLinks.Any(l => l.Source == IdentitySource.Entra))
            {
                throw new IdentityLinkException(
                    IdentityLinkErrorCodes.EntraLinkAlreadyPresent,
                    "This user already has a Microsoft identity linked. Remove the existing link first.");
            }

            var pendingLink = await _entraAssociation.BuildPendingAdminLinkAsync(linkExternalId, cancellationToken);
            if (string.IsNullOrEmpty(pendingLink.TenantId))
            {
                throw new IdentityLinkException(
                    IdentityLinkErrorCodes.InvalidEntraExternalId,
                    "Microsoft identities can only be linked when a directory tenant is configured.");
            }

            var existingEntra = await _userRepository.GetByIdentityLinkAsync(
                ProviderIdentityKey.Entra(pendingLink.TenantId, pendingLink.ExternalId),
                cancellationToken);
            if (existingEntra is not null)
            {
                throw new IdentityLinkException(
                    IdentityLinkErrorCodes.ExternalIdInUse,
                    "An identity link with this source and external id is already attached to another user.");
            }

            await _userRepository.AddIdentityLinkAsync(userId, pendingLink, cancellationToken);
            await AppendAuditAsync(
                EntraLinkAuditActionNames.Pending,
                actorId,
                userId.Value,
                $"userId={userId.Value} oid={pendingLink.ExternalId} tenantId={pendingLink.TenantId}",
                cancellationToken);
            return;
        }

        // A Local link's external id is the user's username; it is not chosen separately.
        if (source == IdentitySource.Local && !string.Equals(linkExternalId, user.Username, StringComparison.Ordinal))
        {
            throw new UsernameException(
                UsernameErrorCodes.Mismatch,
                "A local sign-in must use this user's existing username.");
        }

        var existing = await _userRepository.GetByIdentityLinkAsync(source, linkExternalId, cancellationToken);
        if (existing is not null)
        {
            throw new IdentityLinkException(
                IdentityLinkErrorCodes.ExternalIdInUse,
                "An identity link with this source and external id is already attached to another user.");
        }

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

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is not null && EmergencyAccessIdentity.IsSystemManaged(user))
        {
            if (_emergencyAccess is not null)
                await _emergencyAccess.EnsureDestructiveChangeAllowedAsync(userId, "identity_link.remove", cancellationToken);
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.UnsafeOperation,
                "Recovery Access is system-managed. Identity links cannot be changed through ordinary user administration.");
        }
        if (user is not null && source == IdentitySource.Local)
        {
            if (_emergencyAccess is not null)
                await _emergencyAccess.EnsureDestructiveChangeAllowedAsync(userId, "identity_link.remove_local", cancellationToken);
            var platformAdminRoleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
            var adminCount = await _roleRepository.CountUserAssignmentsAsync(platformAdminRoleId, cancellationToken);
            var userHasPlatformAdmin = user.AssignedRoleIds.Any(r => r.Value == BootstrapConstants.PlatformAdminRoleId);
            var removedLink = user.IdentityLinks.FirstOrDefault(l => l.Source == source && l.ExternalId == linkExternalId);
            if (adminCount == 1 && userHasPlatformAdmin && removedLink is not null && !HasViableAlternateSignIn(user, removedLink))
            {
                await AppendAuditAsync("auth_admin.blocked.last_admin_link_removed", actorId, userId.Value, $"userId={userId.Value} source={source}", cancellationToken);
                throw new IdentityLinkException(
                    IdentityLinkErrorCodes.LastViableSignInMethod,
                    "Cannot remove the last sign-in method for the last platform administrator. A pending Microsoft identity is not a sign-in path until it has been used successfully.");
            }
        }

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
        if (EmergencyAccessIdentity.IsSystemManaged(user))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.UnsafeOperation,
                "Recovery Access is system-managed. Role membership cannot be changed through ordinary user administration.");
        }
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
        if (EmergencyAccessIdentity.IsSystemManaged(user))
        {
            if (_emergencyAccess is not null)
                await _emergencyAccess.EnsureDestructiveChangeAllowedAsync(userId, "role.remove", cancellationToken);
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.UnsafeOperation,
                "Recovery Access is system-managed. Role membership cannot be changed through ordinary user administration.");
        }
        var role = await _roleRepository.GetAsync(roleId, cancellationToken);
        if (role is null)
            throw new InvalidOperationException($"Role {roleId.Value} not found.");

        if (roleId.Value == BootstrapConstants.PlatformAdminRoleId)
        {
            if (_emergencyAccess is not null)
                await _emergencyAccess.EnsureDestructiveChangeAllowedAsync(userId, "role.remove_platform_administrator", cancellationToken);
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
        if (EmergencyAccessIdentity.IsSystemManaged(user))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.UnsafeOperation,
                "Recovery Access is system-managed. The local password must be changed through Emergency Access operator tooling.");
        }
        if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
            throw new InvalidOperationException("User has no Local identity link; cannot set local password.");

        LocalPasswordPolicy.Require(newPassword, user);

        var (hash, salt) = _passwordHasher.HashPassword(newPassword!);
        await _localCredentialRepository.SetPasswordAsync(userId, hash, salt, _passwordHasher.AlgorithmName, cancellationToken);
        if (_actionTokens is not null)
            await _actionTokens.RevokePasswordLifecycleTokensAsync(userId, cancellationToken);
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

        if (!disabled)
        {
            if (_strongAuthentication is null)
            {
                throw new StrongAuthenticationException(
                    StrongAuthenticationErrorCodes.NotConfigured,
                    "Strong authentication is not available. Local sign-in cannot be re-enabled.");
            }

            try
            {
                await _strongAuthentication.RequireRecentStrongAuthenticationAsync(cancellationToken);
            }
            catch (StrongAuthenticationException ex)
            {
                await AppendAuditAsync(
                    StrongAuthenticationAuditActionNames.AssuranceInsufficient,
                    actorId,
                    userId.Value,
                    $"userId={userId.Value} code={ex.Code}",
                    cancellationToken);
                throw;
            }
        }

        if (disabled)
        {
            if (EmergencyAccessIdentity.IsSystemManaged(user))
            {
                if (_emergencyAccess is not null)
                    await _emergencyAccess.EnsureDestructiveChangeAllowedAsync(userId, "credential.disable", cancellationToken);
                throw new EmergencyAccessException(
                    EmergencyAccessErrorCodes.UnsafeOperation,
                    "Recovery Access is system-managed. The local credential cannot be disabled through ordinary user administration.");
            }

            if (_emergencyAccess is not null)
                await _emergencyAccess.EnsureDestructiveChangeAllowedAsync(userId, "credential.disable", cancellationToken);
            var platformAdminRoleId = new RoleId(BootstrapConstants.PlatformAdminRoleId);
            var adminCount = await _roleRepository.CountUserAssignmentsAsync(platformAdminRoleId, cancellationToken);
            var userHasPlatformAdmin = user.AssignedRoleIds.Any(r => r.Value == BootstrapConstants.PlatformAdminRoleId);
            if (adminCount == 1 && userHasPlatformAdmin && !HasViableAlternateSignIn(user, excludedLink: null))
            {
                await AppendAuditAsync("auth_admin.blocked.last_admin_credential_disabled", actorId, userId.Value, $"userId={userId.Value}", cancellationToken);
                throw new InvalidOperationException("Cannot disable the only local credential for the last platform administrator who has no other sign-in path.");
            }
        }

        await _localCredentialRepository.SetDisabledAsync(userId, disabled, cancellationToken);
        var action = disabled
            ? "credential.disabled"
            : StrongAuthenticationAuditActionNames.LocalReenabledStrongAdmin;
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

    private async ValueTask<ActorId> RequireRoleManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.RoleManage, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage roles.");
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

    /// <summary>
    /// An Entra link is a real alternative only when it has been proven by a successful authentication
    /// (<c>LastAuthenticatedAt != null</c>) and Entra is currently usable. A pending administrator
    /// association is not a sign-in path and must not satisfy the last-administrator guard.
    /// </summary>
    private bool HasViableAlternateSignIn(User user, IdentityLink? excludedLink)
    {
        var entraUsable = EntraConfigurationState.IsUsable(_entraOptions?.CurrentValue);
        return user.IdentityLinks.Any(l =>
            l.Source == IdentitySource.Entra
            && l.IsProven
            && entraUsable
            && (excludedLink is null || !ReferenceEquals(l, excludedLink)));
    }

    private static void ValidateEntraExternalId(IdentitySource source, string? externalId)
    {
        if (source != IdentitySource.Entra)
            return;
        if (string.IsNullOrWhiteSpace(externalId))
            throw new IdentityLinkException(
                IdentityLinkErrorCodes.InvalidEntraExternalId,
                "Entra external id must not be null or empty. Use the stable subject (oid or sub) from the token.");
        if (externalId.Contains('@'))
            throw new IdentityLinkException(
                IdentityLinkErrorCodes.InvalidEntraExternalId,
                "Entra external id must be the stable subject (oid or sub), not an email or UPN.");
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
