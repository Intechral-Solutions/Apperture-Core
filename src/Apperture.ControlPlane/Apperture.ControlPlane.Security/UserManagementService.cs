using Apperture.Core.Application;
using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
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
/// <remarks>
/// AUD-1.2 (audit hardening design gate §13): every change runs as one audited unit of work. The reads and safety checks
/// the decision depends on, the change and its audit row commit together; a refusal changes nothing and its row is written
/// after the transaction has rolled back. A request that changes nothing writes no row. A Recovery Access session marks its
/// rows <c>viaRecoveryAccess</c> through the recorder, which replaces the earlier pre-mutation marker row.
/// IAM-10.2a: a change to a role or to a role assignment is decided inside the unit, from the actor's roles and the target role
/// as <see cref="IRoleRepository.LockAuthoritySnapshotAsync"/> locked them. The permission check made before the unit is an
/// advisory fast refusal and never the decision. Rule 1: permissions added to a role must be held by the actor. Rule 2: a role
/// can be assigned or removed only by an actor who holds all of its permissions, and the Platform Administrator role only by a
/// Platform Administrator.
/// </remarks>
public sealed class UserManagementService : IUserManagementService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IPrincipalToUserResolver _principalToUserResolver;
    private readonly IAuthorizationService _auth;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IResourceDescriptorRegistry _descriptorRegistry;
    private readonly ILocalCredentialRepository _localCredentialRepository;
    private readonly ILocalPasswordHasher _passwordHasher;
    private readonly LocalAuthOptions _localOptions;
    private readonly IEntraIdentityAssociationService? _entraAssociation;
    private readonly IOptionsMonitor<EntraAuthOptions>? _entraOptions;
    private readonly IStrongAuthenticationGate? _strongAuthentication;
    private readonly IEmergencyAccessService? _emergencyAccess;
    private readonly IAccountInvitationService? _invitations;
    private readonly IAccountActionTokenService? _actionTokens;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;
    private readonly IScopeContainmentEvaluator _scopeEvaluator;

    public UserManagementService(
        IPrincipalContext principalContext,
        IPrincipalToUserResolver principalToUserResolver,
        IAuthorizationService auth,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IResourceDescriptorRegistry descriptorRegistry,
        ILocalCredentialRepository localCredentialRepository,
        ILocalPasswordHasher passwordHasher,
        IOptions<LocalAuthOptions> localOptions,
        IEntraIdentityAssociationService? entraAssociation = null,
        IOptionsMonitor<EntraAuthOptions>? entraOptions = null,
        IStrongAuthenticationGate? strongAuthentication = null,
        IEmergencyAccessService? emergencyAccess = null,
        IAccountInvitationService? invitations = null,
        IAccountActionTokenService? actionTokens = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null,
        IScopeContainmentEvaluator? scopeEvaluator = null)
    {
        _scopeEvaluator = scopeEvaluator ?? new ScopeContainmentEvaluator();
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
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
        _localOptions = localOptions.Value;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("User administration requires the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("User administration requires the audit event writer.");

    private static AuditedOperation Operation(string primaryAction) => new(primaryAction, AuditActor.CurrentPrincipal);

    private static Task<bool> Done(AuditCommittedOperation committed, CancellationToken cancellationToken) => Task.FromResult(true);

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
        // Advisory only: the decision is made below, from the locked rows.
        await RequirePermissionAssignAsync(cancellationToken);
        var (actor, actorUserId) = await CurrentActorAsync(cancellationToken);

        await Unit.ExecuteRefusableAsync(
            Events,
            Operation(AuditActionNames.PermissionAssignmentUpdated),
            async (transaction, refusals, ct) =>
            {
                var snapshot = await _roleRepository.LockAuthoritySnapshotAsync(actorUserId, roleId, targetUserId: null, ct);
                var authority = RoleAuthorityRules.AuthorityOf(actor, actorUserId, snapshot, _scopeEvaluator);
                if (!authority.Allows(RoleAuthorityRules.PermissionAssign, RoleAuthorityRules.PermissionAssign.Resource, null))
                    throw new UnauthorizedAccessException("Not authorized to assign permissions.");

                var role = snapshot.TargetRole
                    ?? throw new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, roleId.Value);
                if (role.IsSystemRole)
                    return refusals.Refuse<bool>(transaction, AuditEvents.RoleMutationRejected(roleId, "system_role"), SystemRoleRefusal());

                ValidateCatalogPermissions(permissions);

                // Rule 1: only what this replacement adds to the locked role has to be held. Removing needs nothing further.
                var notHeld = RoleAuthorityRules.NotHeld(authority, RoleAuthorityRules.Added(role, permissions));
                if (notHeld.Count > 0)
                    return refusals.Refuse<bool>(transaction, AuditEvents.RoleMutationRejected(roleId, "grant_exceeds_authority", missing: notHeld), GrantExceedsAuthority(notHeld));

                // The diff is the one the replacement committed (D9). An identical set changes nothing and writes no row.
                var change = await _roleRepository.ReplacePermissionsAsync(roleId, permissions, ct);
                if (change.IsEmpty)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                await transaction.StageAsync(
                    AuditEvents.PermissionAssignmentUpdated(roleId, change, permissions.Count, AuditSensitivities.MaxOf(change.Added, _descriptorRegistry)),
                    ct);
                return true;
            },
            Done,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Role> CreateRoleAsync(
        string name,
        string? description = null,
        IReadOnlyList<PermissionDescriptor>? permissions = null,
        CancellationToken cancellationToken = default)
    {
        // Advisory only: the decision is made below, from the locked rows.
        await RequireRoleManageAsync(cancellationToken);
        var grants = permissions ?? Array.Empty<PermissionDescriptor>();
        if (grants.Count > 0)
            await RequirePermissionAssignAsync(cancellationToken);
        var (actor, actorUserId) = await CurrentActorAsync(cancellationToken);

        var normalizedName = NormalizeRoleName(name);
        var normalizedDescription = NormalizeRoleDescription(description);
        ValidateCatalogPermissions(grants);

        // Built once, so a rerun after a confirmed rollback creates the same role.
        var role = new Role(
            NewUserCreatedRoleId(),
            normalizedName,
            normalizedDescription,
            grants,
            IsSystemRole: false);

        return await Unit.ExecuteRefusableAsync(
            Events,
            Operation(AuditActionNames.RoleCreated),
            async (transaction, refusals, ct) =>
            {
                var snapshot = await _roleRepository.LockAuthoritySnapshotAsync(actorUserId, targetRoleId: null, targetUserId: null, ct);
                var authority = RoleAuthorityRules.AuthorityOf(actor, actorUserId, snapshot, _scopeEvaluator);
                if (!authority.Allows(RoleAuthorityRules.RoleManage, RoleAuthorityRules.RoleManage.Resource, null))
                    throw new UnauthorizedAccessException("Not authorized to manage roles.");
                if (grants.Count > 0 && !authority.Allows(RoleAuthorityRules.PermissionAssign, RoleAuthorityRules.PermissionAssign.Resource, null))
                    throw new UnauthorizedAccessException("Not authorized to assign permissions.");

                // Rule 1: every permission a new role starts with has to be held by the actor.
                var notHeld = RoleAuthorityRules.NotHeld(authority, grants);
                if (notHeld.Count > 0)
                    return refusals.Refuse<Role>(transaction, AuditEvents.RoleMutationRejected(role.Id, "grant_exceeds_authority", missing: notHeld), GrantExceedsAuthority(notHeld));

                await EnsureRoleNameAvailableAsync(normalizedName, excludeRoleId: null, ct);
                try
                {
                    await _roleRepository.CreateAsync(role, ct);
                }
                catch (InvalidOperationException ex) when (IsDuplicateRoleName(ex))
                {
                    throw new RoleLifecycleException(RoleLifecycleErrorCodes.DuplicateName, "A role with this name already exists.");
                }

                await transaction.StageAsync(AuditEvents.RoleCreated(role.Id, role.Name, grants, AuditSensitivities.MaxOf(grants, _descriptorRegistry)), ct);
                return role;
            },
            async (_, ct) => await _roleRepository.GetAsync(role.Id, ct) ?? role,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Role> UpdateRoleMetadataAsync(
        RoleId roleId,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        await RequireRoleManageAsync(cancellationToken);

        return await Unit.ExecuteRefusableAsync(
            Events,
            Operation(AuditActionNames.RoleMetadataUpdated),
            async (transaction, refusals, ct) =>
            {
                var role = await _roleRepository.GetAsync(roleId, ct)
                    ?? throw new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, roleId.Value);
                if (role.IsSystemRole)
                    return refusals.Refuse<Role>(transaction, AuditEvents.RoleMutationRejected(roleId, "system_role"), SystemRoleRefusal());

                var normalizedName = NormalizeRoleName(name);
                var normalizedDescription = NormalizeRoleDescription(description);
                var updated = role with { Name = normalizedName, Description = normalizedDescription };
                var descriptionChanged = !string.Equals(role.Description, normalizedDescription, StringComparison.Ordinal);
                if (string.Equals(role.Name, normalizedName, StringComparison.Ordinal) && !descriptionChanged)
                {
                    transaction.DeclareNoOp();
                    return updated;
                }

                await EnsureRoleNameAvailableAsync(normalizedName, roleId, ct);
                try
                {
                    await _roleRepository.UpdateMetadataAsync(roleId, normalizedName, normalizedDescription, ct);
                }
                catch (InvalidOperationException ex) when (IsDuplicateRoleName(ex))
                {
                    throw new RoleLifecycleException(RoleLifecycleErrorCodes.DuplicateName, "A role with this name already exists.");
                }

                await transaction.StageAsync(
                    AuditEvents.RoleMetadataUpdated(roleId, role.Name, normalizedName, descriptionChanged), ct);
                return updated;
            },
            async (committed, ct) =>
                await _roleRepository.GetAsync(roleId, ct)
                ?? new Role(roleId, committed.GetField("newName") ?? NormalizeRoleName(name), NormalizeRoleDescription(description), [], IsSystemRole: false),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DeleteRoleAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        await RequireRoleManageAsync(cancellationToken);

        // A delete whose commit is verified after a lost acknowledgement is reported as done, never as not found (§13.6).
        await Unit.ExecuteRefusableAsync(
            Events,
            Operation(AuditActionNames.RoleDeleted),
            async (transaction, refusals, ct) =>
            {
                var role = await _roleRepository.GetAsync(roleId, ct)
                    ?? throw new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, roleId.Value);
                if (role.IsSystemRole)
                    return refusals.Refuse<bool>(transaction, AuditEvents.RoleMutationRejected(roleId, "system_role"), SystemRoleRefusal());

                // Joins this transaction: the locked assignment count and the delete are part of it.
                var result = await _roleRepository.TryDeleteUnassignedAsync(roleId, ct);
                switch (result.Outcome)
                {
                    case RoleUnassignedDeleteOutcome.NotFound:
                        throw new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, roleId.Value);
                    case RoleUnassignedDeleteOutcome.AssignmentsRemain:
                        return refusals.Refuse<bool>(
                            transaction,
                            AuditEvents.RoleMutationRejected(roleId, "assignments_remain", result.AssignmentCount),
                            new RoleLifecycleException(
                                RoleLifecycleErrorCodes.AssignmentsRemain,
                                "This role still has assignments and cannot be deleted.",
                                result.AssignmentCount));
                    case RoleUnassignedDeleteOutcome.Deleted:
                        await transaction.StageAsync(AuditEvents.RoleDeleted(roleId, result.Name ?? role.Name), ct);
                        return true;
                    default:
                        throw new AuditInvariantException($"Unexpected role delete outcome {result.Outcome}.");
                }
            },
            Done,
            cancellationToken);
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

    /// <inheritdoc />
    public async ValueTask<int> CountRoleAssignmentsAsync(RoleId roleId, CancellationToken cancellationToken = default)
    {
        await RequireRoleManageAsync(cancellationToken);
        return (await _roleRepository.ListUserIdsWithRoleAsync(roleId, cancellationToken)).Count;
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
                    throw new GovernanceValidationException(
                        GovernanceValidationCodes.InvalidPermission,
                        $"Permission at index {i} is not in the canonical governance catalog: Action={p.Action}, Resource={FormatResource(p.Resource)}. " +
                        "Only permissions registered at platform or module startup may be assigned to roles.");
            }
            catch (InvalidOperationException ex)
            {
                // The registry rejects a descriptor it cannot even look up (for example no action).
                throw new GovernanceValidationException(
                    GovernanceValidationCodes.InvalidPermission,
                    $"Permission at index {i} is malformed: {ex.Message}. Action={p.Action}, Resource={FormatResource(p.Resource)}.");
            }
        }
    }

    private static string NormalizeRoleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new GovernanceValidationException(GovernanceValidationCodes.RoleNameRequired, "Role name is required.");
        var trimmed = name.Trim();
        if (trimmed.Length > 256)
            throw new GovernanceValidationException(GovernanceValidationCodes.RoleNameTooLong, "Role name must be 256 characters or fewer.");
        return trimmed;
    }

    private static string? NormalizeRoleDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        var trimmed = description.Trim();
        if (trimmed.Length > 512)
            throw new GovernanceValidationException(GovernanceValidationCodes.RoleDescriptionTooLong, "Role description must be 512 characters or fewer.");
        return trimmed;
    }

    /// <summary>The acting principal and the user it resolved to (null when it resolved to none).</summary>
    private async ValueTask<(IActor? Actor, UserId? UserId)> CurrentActorAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken);
        if (actor is null)
            return (null, null);
        return (actor, await _principalToUserResolver.ResolveUserIdAsync(actor, cancellationToken));
    }

    private static AuthorizationRefusalException GrantExceedsAuthority(IReadOnlyList<PermissionDescriptor> notHeld) =>
        new(
            AuthorizationRefusalCodes.GrantExceedsAuthority,
            "You cannot grant permissions you do not hold.",
            notHeld);

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

    private static RoleLifecycleException SystemRoleRefusal() =>
        new(RoleLifecycleErrorCodes.SystemRole, "This built-in role cannot be changed or deleted.");

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
        await RequireUserManageAsync(cancellationToken);
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
            if (sendSetupInvitation && (_invitations is null || _actionTokens is null))
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

        // Built once, so a rerun after a confirmed rollback creates the same user with the same credential. The salted hash
        // is computed here and never leaves this method.
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
        var credentialSet = source == IdentitySource.Local && !string.IsNullOrEmpty(password);
        var credential = credentialSet ? _passwordHasher.HashPassword(password!) : default;
        var invite = sendSetupInvitation && source == IdentitySource.Local;
        // The invitation link is chosen once, like the user id, so a rerun issues the same link (AUD-1.2 D8).
        var invitationToken = invite ? _actionTokens!.Prepare() : null;
        PreparedSetupInvitation? invitation = null;

        // The user, its credential or invitation and their rows commit together: no user without the credential or the
        // invitation it was created with (AUD-1.2). The invitation link is mailed once, after commit.
        await Unit.ExecuteAsync(
            Operation(AuditActionNames.UserCreated),
            async (transaction, ct) =>
            {
                invitation = null;
                var existing = await _userRepository.GetByIdentityLinkAsync(source, linkExternalId, ct);
                if (existing is not null)
                    throw new InvalidOperationException("An identity link with this source and external id already exists.");

                await _userRepository.AddAsync(user, ct);
                if (credentialSet)
                    await _localCredentialRepository.SetPasswordAsync(userId, credential.Hash, credential.Salt, _passwordHasher.AlgorithmName, ct);

                await transaction.StageAsync(
                    AuditEvents.UserCreated(userId, linkExternalId, user.DisplayName, credentialSet, sendSetupInvitation), ct);
                if (invite)
                    invitation = await _invitations!.StageSetupInvitationAsync(transaction, userId, contactEmail, invitationToken!, ct);
                return true;
            },
            Done,
            cancellationToken);

        // The result is not part of this method's contract; a failed or unknown send is recorded by its own row and shown in
        // the user's invitation state, and never reported as a failure of the committed creation.
        if (invitation is not null)
            await _invitations!.DeliverSetupInvitationAsync(invitation, AuditActor.CurrentPrincipal, cancellationToken);

        return userId;
    }

    /// <inheritdoc />
    public async ValueTask<AccountInvitationResult> SendSetupInvitationAsync(
        UserId userId,
        string? contactEmail = null,
        CancellationToken cancellationToken = default)
    {
        await RequireUserManageAsync(cancellationToken);
        if (_invitations is null)
            throw new InvalidOperationException("Apperture setup invitations are not available.");

        // The address falls back to the user's pending, then verified, one inside the invitation's transaction.
        return await _invitations.SendSetupInvitationAsync(userId, contactEmail, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<UsernameRenameResult> RenameUsernameAsync(
        UserId userId,
        string requestedUsername,
        CancellationToken cancellationToken = default)
    {
        await RequireUserManageAsync(cancellationToken);

        // AUD-1.1: one audited unit of work (audit hardening design gate §13). Policy, Recovery Access refusal, ownership
        // and the ledger/Local-link change run inside the unit's transaction, and the audit row commits with them. The
        // audited names come from the transaction's own result, never from a read made before it. After an uncertain
        // commit the outcome is verified before anything reruns, so a rename that already committed is reported as
        // done (from its recorded names), never as unchanged.
        var unit = _auditedUnitOfWork
            ?? throw new InvalidOperationException("Renaming a user requires the audited unit of work.");
        return await unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.UsernameChanged, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var result = await new UsernameService(_userRepository).RenameWithResultAsync(userId, requestedUsername, ct);
                if (result.Outcome == UsernameChangeOutcome.Unchanged)
                {
                    transaction.DeclareNoOp();
                    return result;
                }

                await transaction.StageAsync(
                    AuditEvents.UsernameChanged(userId, result.PreviousUsername, result.Username, result.Outcome), ct);
                return result;
            },
            (committed, _) => Task.FromResult(ReconstructRename(committed)),
            cancellationToken);
    }

    /// <summary>A committed rename's result, rebuilt from its audit row without rerunning the rename (§13.5).</summary>
    internal static UsernameRenameResult ReconstructRename(AuditCommittedOperation committed)
    {
        var previous = committed.GetField("previousUsername")
            ?? throw new InvalidOperationException("The committed rename row records no previous username.");
        var next = committed.GetField("newUsername")
            ?? throw new InvalidOperationException("The committed rename row records no new username.");
        var outcome = committed.GetField("change") == "reclaimed" ? UsernameChangeOutcome.Reclaimed : UsernameChangeOutcome.Assigned;
        return new UsernameRenameResult(outcome, previous, next);
    }

    /// <inheritdoc />
    public async ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string? externalId, CancellationToken cancellationToken = default)
    {
        await RequireUserManageAsync(cancellationToken);
        ValidateEntraExternalId(source, externalId);

        // The pending Microsoft link is built from a directory lookup: an external read, so it runs before the transaction.
        // The same checks as before run first, so an absent user or an existing Microsoft link is still reported ahead of
        // any directory error; the transaction then re-checks everything against the state it commits against.
        IdentityLink? pendingLink = null;
        if (source == IdentitySource.Entra && _entraAssociation is not null)
        {
            var current = await RequireLinkableUserAsync(userId, cancellationToken);
            var externalKey = externalId!.Trim();
            if (current.IdentityLinks.Any(l => l.Source == source && l.ExternalId == externalKey))
                return;
            RequireNoEntraLink(current);
            pendingLink = await _entraAssociation.BuildPendingAdminLinkAsync(externalKey, cancellationToken);
            if (string.IsNullOrEmpty(pendingLink.TenantId))
            {
                throw new IdentityLinkException(
                    IdentityLinkErrorCodes.InvalidEntraExternalId,
                    "Microsoft identities can only be linked when a directory tenant is configured.");
            }
        }

        // Generated once, so a rerun after a confirmed rollback stores, and targets, the same link.
        var linkId = IdentityLink.NewLinkId();
        await Unit.ExecuteAsync(
            Operation(pendingLink is not null ? AuditActionNames.EntraLinkPending : AuditActionNames.IdentityLinkAdded),
            async (transaction, ct) =>
            {
                var user = await RequireLinkableUserAsync(userId, ct);

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

                // Already linked: nothing changes and nothing is recorded; the request still succeeds.
                if (user.IdentityLinks.Any(l => l.Source == source && l.ExternalId == linkExternalId))
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                if (pendingLink is not null)
                {
                    RequireNoEntraLink(user);
                    var existingEntra = await _userRepository.GetByIdentityLinkAsync(
                        ProviderIdentityKey.Entra(pendingLink.TenantId!, pendingLink.ExternalId),
                        ct);
                    if (existingEntra is not null)
                        throw ExternalIdInUse();

                    await _userRepository.AddIdentityLinkAsync(userId, pendingLink with { LinkId = linkId }, ct);
                    await transaction.StageAsync(
                        AuditEvents.EntraLinkPending(userId, linkId, pendingLink.TenantId!, pendingLink.ExternalId), ct);
                    return true;
                }

                // A Local link's external id is the user's username; it is not chosen separately.
                if (source == IdentitySource.Local && !string.Equals(linkExternalId, user.Username, StringComparison.Ordinal))
                {
                    throw new UsernameException(
                        UsernameErrorCodes.Mismatch,
                        "A local sign-in must use this user's existing username.");
                }

                var existing = await _userRepository.GetByIdentityLinkAsync(source, linkExternalId, ct);
                if (existing is not null)
                    throw ExternalIdInUse();

                if (source == IdentitySource.Local)
                {
                    await _userRepository.AddIdentityLinkAsync(
                        userId, new IdentityLink(source, linkExternalId, LastAuthenticatedAt: null, LinkId: linkId), ct);
                }
                else
                {
                    // A Microsoft link without the directory association needs a tenant the store requires; the store
                    // refuses it, as it always has.
                    await _userRepository.AddIdentityLinkAsync(userId, source, linkExternalId, ct);
                }

                await transaction.StageAsync(AuditEvents.IdentityLinkAdded(userId, linkId, source, linkExternalId), ct);
                return true;
            },
            Done,
            cancellationToken);
    }

    private async ValueTask<User> RequireLinkableUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new IdentityLinkException(IdentityLinkErrorCodes.UserNotFound, $"User {userId.Value} not found.");
        if (EmergencyAccessIdentity.IsSystemManaged(user))
        {
            throw new EmergencyAccessException(
                EmergencyAccessErrorCodes.UnsafeOperation,
                "Recovery Access is system-managed. Identity links cannot be changed through ordinary user administration.");
        }

        return user;
    }

    private static void RequireNoEntraLink(User user)
    {
        if (user.IdentityLinks.Any(l => l.Source == IdentitySource.Entra))
        {
            throw new IdentityLinkException(
                IdentityLinkErrorCodes.EntraLinkAlreadyPresent,
                "This user already has a Microsoft identity linked. Remove the existing link first.");
        }
    }

    private static IdentityLinkException ExternalIdInUse() =>
        new(IdentityLinkErrorCodes.ExternalIdInUse, "An identity link with this source and external id is already attached to another user.");

    /// <inheritdoc />
    public async ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
    {
        await RequireUserManageAsync(cancellationToken);
        var linkExternalId = source == IdentitySource.Local
            ? LocalAuthHelpers.NormalizeUsername(externalId)
            : externalId.Trim();

        await Unit.ExecuteRefusableAsync(
            Events,
            Operation(AuditActionNames.IdentityLinkRemoved),
            async (transaction, refusals, ct) =>
            {
                var user = await _userRepository.GetByIdAsync(userId, ct);
                if (user is not null && EmergencyAccessIdentity.IsSystemManaged(user))
                {
                    if (await IsRecoveryAccessUserAsync(userId, ct))
                        return refusals.Refuse<bool>(transaction, AuditEvents.UnsafeOperationBlocked(userId, "identity_link.remove"), EmergencyAccessService.UnsafeOperationRefusal());
                    throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.UnsafeOperation,
                        "Recovery Access is system-managed. Identity links cannot be changed through ordinary user administration.");
                }

                var removedLink = user?.IdentityLinks.FirstOrDefault(l => l.Source == source && l.ExternalId == linkExternalId);
                if (user is not null && source == IdentitySource.Local)
                {
                    if (await IsRecoveryAccessUserAsync(userId, ct))
                        return refusals.Refuse<bool>(transaction, AuditEvents.UnsafeOperationBlocked(userId, "identity_link.remove_local"), EmergencyAccessService.UnsafeOperationRefusal());
                    var adminCount = await _roleRepository.CountUserAssignmentsAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), ct);
                    var userHasPlatformAdmin = user.AssignedRoleIds.Any(RoleAuthorityRules.IsPlatformAdministratorRole);
                    if (adminCount == 1 && userHasPlatformAdmin && removedLink is not null && !HasViableAlternateSignIn(user, removedLink))
                    {
                        return refusals.Refuse<bool>(
                            transaction,
                            AuditEvents.BlockedLastAdminLinkRemoved(userId, LinkTargetId(removedLink), source),
                            new IdentityLinkException(
                                IdentityLinkErrorCodes.LastViableSignInMethod,
                                "Cannot remove the last sign-in method for the last platform administrator. A pending Microsoft identity is not a sign-in path until it has been used successfully."));
                    }
                }

                // No such link: nothing changes and nothing is recorded; the request still succeeds, as it always has.
                if (removedLink is null)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                await _userRepository.RemoveIdentityLinkAsync(userId, source, linkExternalId, ct);
                await transaction.StageAsync(
                    AuditEvents.IdentityLinkRemoved(userId, LinkTargetId(removedLink), source, linkExternalId, removedLink.TenantId), ct);
                return true;
            },
            Done,
            cancellationToken);
    }

    /// <summary>
    /// The audit target of a link: its persisted id. A store that does not persist link ids (a test double) has none, and
    /// its external id stands in.
    /// </summary>
    private static string LinkTargetId(IdentityLink link) => link.LinkId ?? link.ExternalId;

    /// <summary>
    /// True when the user is the designated Recovery Access account. Read inside the operation's transaction, so the safety
    /// check sees the state the change would commit against; a refusal is recorded by the caller after the rollback.
    /// </summary>
    private async ValueTask<bool> IsRecoveryAccessUserAsync(UserId userId, CancellationToken cancellationToken) =>
        _emergencyAccess is not null && await _emergencyAccess.IsDesignatedEmergencyUserAsync(userId, cancellationToken);

    /// <inheritdoc />
    public async ValueTask AssignRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        // Advisory only: the decision is made below, from the locked rows.
        await RequireRoleAssignAsync(cancellationToken);
        var (actor, actorUserId) = await CurrentActorAsync(cancellationToken);

        await Unit.ExecuteRefusableAsync(
            Events,
            Operation(AuditActionNames.RoleAssigned),
            async (transaction, refusals, ct) =>
            {
                // 1. The lock set and the decision snapshot.
                var snapshot = await _roleRepository.LockAuthoritySnapshotAsync(actorUserId, roleId, userId, ct);
                var authority = RoleAuthorityRules.AuthorityOf(actor, actorUserId, snapshot, _scopeEvaluator);

                // 2. Authorization refusals, before anything that could make the request a no-op.
                if (!authority.Allows(RoleAuthorityRules.RoleAssign, RoleAuthorityRules.RoleAssign.Resource, null))
                    throw new UnauthorizedAccessException("Not authorized to assign roles.");
                var role = snapshot.TargetRole
                    ?? throw new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, roleId.Value);
                // From here on the role is the locked row, and its own id is the only id used: on a case-insensitive catalog a request
                // can spell the id in another case and still find the row, but every decision, stored assignment and audit row
                // names the role by the id the row carries.
                var lockedRoleId = role.Id;
                if (RoleAuthorityRules.CheckEntitlement(authority, role, removal: false) is { } refused)
                    return refusals.Refuse<bool>(transaction, AuditEvents.RoleAssignmentRejected(userId, lockedRoleId, refused.Reason, refused.Missing), refused.ToException());

                // 3. A system-managed target (Recovery Access).
                var user = await _userRepository.GetByIdAsync(userId, ct)
                    ?? throw new GovernanceNotFoundException(GovernanceNotFoundKinds.User, userId.Value);
                if (EmergencyAccessIdentity.IsSystemManaged(user))
                {
                    throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.UnsafeOperation,
                        "Recovery Access is system-managed. Role membership cannot be changed through ordinary user administration.");
                }

                // 4. Already assigned (read under the assignment key lock): nothing changes and nothing is recorded; the request still succeeds.
                if (snapshot.TargetAssignmentExists)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                // 5. State refusals, for a real change only: a role whose stored permissions cannot be read is not assigned, because it
                // looks empty and would silently gain permissions once the data is repaired.
                if (!role.PermissionsReadable)
                {
                    return refusals.Refuse<bool>(
                        transaction,
                        AuditEvents.RoleAssignmentRejected(userId, lockedRoleId, "role_permissions_unreadable"),
                        new GovernanceConflictException(
                            GovernanceConflictCodes.RolePermissionsUnreadable,
                            "This role's permissions could not be read, so it cannot be assigned. Replace its permissions first."));
                }

                await _roleRepository.AssignToUserAsync(userId, lockedRoleId, ct);
                await transaction.StageAsync(AuditEvents.RoleAssigned(userId, lockedRoleId, AuditSensitivities.MaxOf(role.Permissions, _descriptorRegistry)), ct);
                return true;
            },
            Done,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RemoveRoleAsync(UserId userId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        // Advisory only: the decision is made below, from the locked rows.
        await RequireRoleAssignAsync(cancellationToken);
        var (actor, actorUserId) = await CurrentActorAsync(cancellationToken);

        await Unit.ExecuteRefusableAsync(
            Events,
            Operation(AuditActionNames.RoleRemoved),
            async (transaction, refusals, ct) =>
            {
                // 1. The lock set and the decision snapshot.
                var snapshot = await _roleRepository.LockAuthoritySnapshotAsync(actorUserId, roleId, userId, ct);
                var authority = RoleAuthorityRules.AuthorityOf(actor, actorUserId, snapshot, _scopeEvaluator);

                // 2. Authorization refusals, before anything that could make the request a no-op. Removal needs the same
                // entitlement as assignment; a user removing a role from themselves holds it, so needs nothing more.
                if (!authority.Allows(RoleAuthorityRules.RoleAssign, RoleAuthorityRules.RoleAssign.Resource, null))
                    throw new UnauthorizedAccessException("Not authorized to assign roles.");
                var role = snapshot.TargetRole
                    ?? throw new GovernanceNotFoundException(GovernanceNotFoundKinds.Role, roleId.Value);
                // The locked row's own id from here on, as in assignment: a request may spell the id in another case.
                var lockedRoleId = role.Id;
                if (RoleAuthorityRules.CheckEntitlement(authority, role, removal: true) is { } refused)
                    return refusals.Refuse<bool>(transaction, AuditEvents.RoleAssignmentRejected(userId, lockedRoleId, refused.Reason, refused.Missing), refused.ToException());

                // 3. A system-managed target (Recovery Access).
                var user = await _userRepository.GetByIdAsync(userId, ct)
                    ?? throw new GovernanceNotFoundException(GovernanceNotFoundKinds.User, userId.Value);
                if (EmergencyAccessIdentity.IsSystemManaged(user))
                {
                    if (await IsRecoveryAccessUserAsync(userId, ct))
                        return refusals.Refuse<bool>(transaction, AuditEvents.UnsafeOperationBlocked(userId, "role.remove"), EmergencyAccessService.UnsafeOperationRefusal());
                    throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.UnsafeOperation,
                        "Recovery Access is system-managed. Role membership cannot be changed through ordinary user administration.");
                }

                if (RoleAuthorityRules.IsPlatformAdministratorRole(lockedRoleId) && await IsRecoveryAccessUserAsync(userId, ct))
                    return refusals.Refuse<bool>(transaction, AuditEvents.UnsafeOperationBlocked(userId, "role.remove_platform_administrator"), EmergencyAccessService.UnsafeOperationRefusal());

                // 4. Not assigned (read under the assignment key lock): nothing changes and nothing is recorded; the request still succeeds.
                if (!snapshot.TargetAssignmentExists)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                // 5. State refusals, for a real change only. The last-administrator guard is unchanged here: it counts assignments
                // and is replaced by the shared viability invariant in IAM-10.2b.
                if (RoleAuthorityRules.IsPlatformAdministratorRole(lockedRoleId))
                {
                    var count = await _roleRepository.CountUserAssignmentsAsync(lockedRoleId, ct);
                    if (count <= 1)
                    {
                        return refusals.Refuse<bool>(
                            transaction,
                            AuditEvents.BlockedLastPlatformAdminRemoval(userId, lockedRoleId),
                            new GovernanceConflictException(
                                GovernanceConflictCodes.LastViableAdministrator,
                                "Cannot remove the last platform administrator role assignment."));
                    }
                }

                await _roleRepository.RemoveFromUserAsync(userId, lockedRoleId, ct);
                await transaction.StageAsync(AuditEvents.RoleRemoved(userId, lockedRoleId), ct);
                return true;
            },
            Done,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SetLocalPasswordAsync(UserId userId, string newPassword, CancellationToken cancellationToken = default)
    {
        await RequireCredentialManageAsync(cancellationToken);

        // The salted hash is computed once and reused by a rerun; it never leaves this method.
        (string Hash, string Salt)? credential = null;
        await Unit.ExecuteAsync(
            Operation(AuditActionNames.PasswordSet),
            async (transaction, ct) =>
            {
                var user = await _userRepository.GetByIdAsync(userId, ct)
                    ?? throw new InvalidOperationException($"User {userId.Value} not found.");
                if (EmergencyAccessIdentity.IsSystemManaged(user))
                {
                    throw new EmergencyAccessException(
                        EmergencyAccessErrorCodes.UnsafeOperation,
                        "Recovery Access is system-managed. The local password must be changed through Emergency Access operator tooling.");
                }
                if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
                    throw new InvalidOperationException("User has no Local identity link; cannot set local password.");

                LocalPasswordPolicy.Require(newPassword, user);

                credential ??= _passwordHasher.HashPassword(newPassword!);
                await _localCredentialRepository.SetPasswordAsync(userId, credential.Value.Hash, credential.Value.Salt, _passwordHasher.AlgorithmName, ct);
                if (_actionTokens is not null)
                    await _actionTokens.RevokePasswordLifecycleTokensAsync(userId, ct);
                await transaction.StageAsync(AuditEvents.PasswordSet(userId), ct);
                return true;
            },
            Done,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SetLocalCredentialDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
    {
        await RequireCredentialManageAsync(cancellationToken);

        await Unit.ExecuteRefusableAsync(
            Events,
            Operation(disabled ? AuditActionNames.CredentialDisabled : AuditActionNames.LocalCredentialReenabled),
            async (transaction, refusals, ct) =>
            {
                var user = await _userRepository.GetByIdAsync(userId, ct)
                    ?? throw new InvalidOperationException($"User {userId.Value} not found.");
                if (!user.IdentityLinks.Any(l => l.Source == IdentitySource.Local))
                    throw new InvalidOperationException("User has no Local identity link; cannot manage local credential.");

                var cred = await _localCredentialRepository.GetByUserIdAsync(userId, ct)
                    ?? throw new InvalidOperationException("User has no local credential row; set a password first.");

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
                        await _strongAuthentication.RequireRecentStrongAuthenticationAsync(ct);
                    }
                    catch (StrongAuthenticationException ex)
                    {
                        return refusals.Refuse<bool>(transaction, AuditEvents.StrongAuthenticationInsufficient(userId, ex.Code), ex);
                    }
                }
                else
                {
                    if (EmergencyAccessIdentity.IsSystemManaged(user))
                    {
                        if (await IsRecoveryAccessUserAsync(userId, ct))
                            return refusals.Refuse<bool>(transaction, AuditEvents.UnsafeOperationBlocked(userId, "credential.disable"), EmergencyAccessService.UnsafeOperationRefusal());
                        throw new EmergencyAccessException(
                            EmergencyAccessErrorCodes.UnsafeOperation,
                            "Recovery Access is system-managed. The local credential cannot be disabled through ordinary user administration.");
                    }

                    if (await IsRecoveryAccessUserAsync(userId, ct))
                        return refusals.Refuse<bool>(transaction, AuditEvents.UnsafeOperationBlocked(userId, "credential.disable"), EmergencyAccessService.UnsafeOperationRefusal());
                    var adminCount = await _roleRepository.CountUserAssignmentsAsync(new RoleId(BootstrapConstants.PlatformAdminRoleId), ct);
                    var userHasPlatformAdmin = user.AssignedRoleIds.Any(RoleAuthorityRules.IsPlatformAdministratorRole);
                    if (adminCount == 1 && userHasPlatformAdmin && !HasViableAlternateSignIn(user, excludedLink: null))
                    {
                        return refusals.Refuse<bool>(
                            transaction,
                            AuditEvents.BlockedLastAdminCredentialDisabled(userId),
                            new InvalidOperationException("Cannot disable the only local credential for the last platform administrator who has no other sign-in path."));
                    }
                }

                // Already in the requested state: nothing changes and nothing is recorded; the request still succeeds.
                if (cred.IsDisabled == disabled)
                {
                    transaction.DeclareNoOp();
                    return true;
                }

                await _localCredentialRepository.SetDisabledAsync(userId, disabled, ct);
                await transaction.StageAsync(
                    disabled ? AuditEvents.CredentialDisabled(userId) : AuditEvents.LocalCredentialReenabled(userId), ct);
                return true;
            },
            Done,
            cancellationToken);
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

}
