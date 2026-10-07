using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Adds people from the Microsoft directory or from approved access requests.
/// </summary>
/// <remarks>
/// AUD-1.2: each provisioning is one audited unit of work of the administrator. Directory reads run before it; the user,
/// its link, the initial role assignment, the access request's approval or reconciliation and their rows commit together;
/// the welcome mail is sent once after commit with its own W3 row. Approving a request whose identity already has a user
/// commits the reconciliation and is refused only after that commit was confirmed (clarification 4).
/// </remarks>
public sealed class UserProvisioningService : IUserProvisioningService
{
    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly IEntraProvisioningDirectoryService _directory;
    private readonly IEntraIdentityAssociationService _association;
    private readonly IEntraAccessRequestRepository _requests;
    private readonly TimeProvider _clock;
    private readonly IAccountSecurityMailSender _mail;
    private readonly AccountSecurityLinkBuilder _links;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;
    private readonly IAuditEventWriter? _auditEvents;

    public UserProvisioningService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IUserRepository users,
        IRoleRepository roles,
        IEntraInstanceRegistrationRepository registrations,
        IEntraProvisioningDirectoryService directory,
        IEntraIdentityAssociationService association,
        IEntraAccessRequestRepository requests,
        TimeProvider clock,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links,
        IAuditedUnitOfWork? auditedUnitOfWork = null,
        IAuditEventWriter? auditEvents = null)
    {
        _principalContext = principalContext;
        _auth = auth;
        _users = users;
        _roles = roles;
        _registrations = registrations;
        _directory = directory;
        _association = association;
        _requests = requests;
        _clock = clock;
        _mail = mail;
        _links = links;
        _auditedUnitOfWork = auditedUnitOfWork;
        _auditEvents = auditEvents;
    }

    public async ValueTask<UserProvisioningResult> ProvisionFromEntraDirectoryAsync(
        EntraDirectoryProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        await RequireRoleAssignIfRequestedAsync(request.InitialRoleId, cancellationToken);

        var objectId = EntraTokenClaims.Normalize(request.ObjectId);
        if (!EntraTokenClaims.IsValidObjectId(objectId))
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.InvalidObjectId,
                "Choose a Microsoft directory user. Email and user principal name are not identity keys.");
        }

        var registration = await _registrations.GetByIdAsync(request.RegistrationId, cancellationToken)
            ?? throw new UserProvisioningException(
                UserProvisioningErrorCodes.RegistrationNotFound,
                "That Microsoft directory registration was not found.");
        if (!registration.Enabled)
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.RegistrationDisabled,
                "That Microsoft directory registration is disabled.");
        }

        var tenantId = EntraTokenClaims.Normalize(EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration));
        if (string.IsNullOrEmpty(tenantId))
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.RegistrationNotFound,
                "That Microsoft directory registration does not have a tenant id.");
        }

        var existing = await _users.GetByIdentityLinkAsync(
            ProviderIdentityKey.Entra(tenantId, objectId!),
            cancellationToken);
        if (existing is not null)
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.DuplicateIdentity,
                "That Microsoft identity is already an Apperture user.");
        }

        var directoryUser = await _directory.GetByObjectIdAsync(registration.Id, objectId!, cancellationToken)
            ?? throw new UserProvisioningException(
                UserProvisioningErrorCodes.DirectoryUserMissing,
                "That person is no longer in the Microsoft directory.");
        if (!directoryUser.AccountEnabled)
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.DirectoryUserDisabled,
                "Disabled Microsoft directory accounts cannot be added.");
        }

        var displayName = string.IsNullOrWhiteSpace(directoryUser.DisplayName)
            ? throw new UserProvisioningException(UserProvisioningErrorCodes.DisplayNameRequired, "A display name is required.")
            : directoryUser.DisplayName.Trim();

        // Directory reads are external calls and run before the transaction; the user id and link id are chosen once.
        var pendingLink = await _association.BuildPendingAdminLinkAsync(objectId!, registration, cancellationToken);
        pendingLink = pendingLink with { LinkId = pendingLink.LinkId ?? IdentityLink.NewLinkId() };
        var userId = NewUserId();
        var reviewedAt = _clock.GetUtcNow();

        // The user, its link, the initial role, the reconciled access request and their rows commit together (AUD-1.2).
        var (created, roleAssigned, reconciled) = await Unit.ExecuteAsync(
            Operation(AuditActionNames.ProvisionedFromDirectory),
            async (transaction, ct) =>
            {
                var current = await _registrations.GetByIdAsync(registration.Id, ct);
                if (current is null || !current.Enabled)
                {
                    throw new UserProvisioningException(
                        UserProvisioningErrorCodes.RegistrationDisabled,
                        "That Microsoft directory registration is disabled.");
                }

                var user = await AddWithGeneratedUsernameAsync(
                    username => new User(userId, displayName, [pendingLink], [], IsBreakGlassEligible: false) { Username = username },
                    new UsernameGenerationSources(directoryUser.UserPrincipalName, directoryUser.Mail, displayName),
                    ct);
                await transaction.StageAsync(
                    AuditEvents.ProvisionedFromDirectory(userId, user.Username, tenantId, objectId!, registration.Id), ct);

                var assigned = await TryAssignRoleAsync(transaction, userId, request.InitialRoleId, ct);
                var reconciledId = await ReconcilePendingAsync(transaction, tenantId, objectId!, userId, actorId, reviewedAt, ct);
                return (user, assigned, reconciledId);
            },
            async (_, ct) =>
            {
                var user = await _users.GetByIdAsync(userId, ct)
                    ?? throw new InvalidOperationException($"User {userId.Value} was verified as created but could not be read.");
                var latest = await _requests.FindLatestAsync(tenantId, objectId!, ct);
                return (user, !string.IsNullOrWhiteSpace(request.InitialRoleId), latest?.ProvisionedUserId == userId.Value ? latest.Id : null);
            },
            cancellationToken);

        var welcome = await TrySendWelcomeAsync(
            userId,
            request.SendWelcomeEmail,
            directoryUser.Mail,
            displayName,
            registration.DisplayName,
            cancellationToken);

        return new UserProvisioningResult(
            userId,
            created.Username,
            displayName,
            objectId!,
            EntraLinkProven: false,
            welcome.Attempted,
            welcome.Delivered,
            welcome.Failure,
            roleAssigned,
            reconciled);
    }

    public async ValueTask<UserProvisioningResult> ProvisionFromAccessRequestAsync(
        AccessRequestApprovalProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        await RequireRoleAssignIfRequestedAsync(request.InitialRoleId, cancellationToken);

        var userId = NewUserId();
        var linkId = IdentityLink.NewLinkId();
        var reviewedAt = _clock.GetUtcNow();
        var approval = await Unit.ExecuteAsync(
            new AuditedOperation(
                AuditActionNames.ProvisionedFromAccessRequest,
                AuditActor.CurrentPrincipal,
                [AuditActionNames.AccessRequestReconciled]),
            async (transaction, ct) =>
            {
                // Every check the approval depends on is read in this transaction.
                var accessRequest = await _requests.GetByIdAsync(request.RequestId, ct)
                    ?? throw new UserProvisioningException(
                        UserProvisioningErrorCodes.RequestNotFound,
                        "That access request was not found.");
                if (accessRequest.Status != EntraAccessRequestStatus.Pending)
                {
                    throw new UserProvisioningException(
                        UserProvisioningErrorCodes.RequestNotPending,
                        "That access request is no longer pending.");
                }

                var objectId = EntraTokenClaims.Normalize(accessRequest.ProviderObjectId);
                var tenantId = EntraTokenClaims.Normalize(accessRequest.TenantId);
                if (string.IsNullOrEmpty(tenantId) || !EntraTokenClaims.IsValidObjectId(objectId))
                {
                    throw new UserProvisioningException(
                        UserProvisioningErrorCodes.InvalidObjectId,
                        "That access request does not have a Microsoft tenant and object id.");
                }

                var existing = await _users.GetByIdentityLinkAsync(ProviderIdentityKey.Entra(tenantId, objectId!), ct);
                if (existing is not null)
                {
                    // The identity already belongs to a user: the request is closed against that user and committed, and only
                    // then is the approval refused (clarification 4).
                    await _requests.UpdateAsync(
                        accessRequest with
                        {
                            Status = EntraAccessRequestStatus.Approved,
                            ReviewedAtUtc = reviewedAt,
                            ReviewedByUserId = actorId.Value,
                            ProvisionedUserId = existing.Id.Value
                        },
                        ct);
                    await transaction.StageAsync(AuditEvents.AccessRequestReconciled(accessRequest.Id, tenantId, objectId!, existing.Id), ct);
                    return Approval.Reconciled;
                }

                var displayName = string.IsNullOrWhiteSpace(accessRequest.DisplayNameSnapshot)
                    ? "Microsoft user"
                    : accessRequest.DisplayNameSnapshot.Trim();
                var link = new IdentityLink(
                    IdentitySource.Entra,
                    objectId!,
                    LastAuthenticatedAt: accessRequest.AuthenticatedAtUtc,
                    TenantId: accessRequest.TenantId,
                    RegistrationId: accessRequest.RegistrationId,
                    LinkedAt: reviewedAt,
                    ProviderSubject: null,
                    DisplayNameSnapshot: accessRequest.DisplayNameSnapshot,
                    UserPrincipalNameSnapshot: accessRequest.UserPrincipalNameSnapshot,
                    MailSnapshot: accessRequest.MailSnapshot,
                    LinkId: linkId);

                var created = await AddWithGeneratedUsernameAsync(
                    username => new User(userId, displayName, [link], [], IsBreakGlassEligible: false) { Username = username },
                    new UsernameGenerationSources(
                        accessRequest.UserPrincipalNameSnapshot,
                        accessRequest.MailSnapshot,
                        accessRequest.DisplayNameSnapshot),
                    ct);
                await transaction.StageAsync(
                    AuditEvents.ProvisionedFromAccessRequest(userId, created.Username, tenantId, objectId!, accessRequest.Id), ct);

                var roleAssigned = await TryAssignRoleAsync(transaction, userId, request.InitialRoleId, ct);
                await _requests.UpdateAsync(
                    accessRequest with
                    {
                        Status = EntraAccessRequestStatus.Approved,
                        ReviewedAtUtc = reviewedAt,
                        ReviewedByUserId = actorId.Value,
                        ProvisionedUserId = userId.Value
                    },
                    ct);
                await transaction.StageAsync(AuditEvents.AccessRequestApproved(accessRequest.Id, tenantId, objectId!, userId), ct);
                return new Approval(accessRequest, created, displayName, roleAssigned);
            },
            async (committed, ct) =>
            {
                if (committed.Action == AuditActionNames.AccessRequestReconciled)
                    return Approval.Reconciled;
                var accessRequest = await _requests.GetByIdAsync(request.RequestId, ct);
                var user = await _users.GetByIdAsync(userId, ct);
                if (accessRequest is null || user is null)
                    throw new InvalidOperationException($"Access request {request.RequestId} was verified as approved but could not be read.");
                return new Approval(accessRequest, user, user.DisplayName, !string.IsNullOrWhiteSpace(request.InitialRoleId));
            },
            cancellationToken);

        if (ReferenceEquals(approval, Approval.Reconciled))
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.DuplicateIdentity,
                "That Microsoft identity is already an Apperture user.");
        }

        var registration = await _registrations.GetByIdAsync(approval.Request!.RegistrationId, cancellationToken);
        var welcome = await TrySendWelcomeAsync(
            userId,
            request.SendWelcomeEmail,
            approval.Request.MailSnapshot,
            approval.DisplayName!,
            registration?.DisplayName,
            cancellationToken);

        return new UserProvisioningResult(
            userId,
            approval.User!.Username,
            approval.DisplayName!,
            EntraTokenClaims.Normalize(approval.Request.ProviderObjectId)!,
            EntraLinkProven: true,
            welcome.Attempted,
            welcome.Delivered,
            welcome.Failure,
            approval.RoleAssigned,
            approval.Request.Id);
    }

    /// <summary>What an approval's transaction committed. <see cref="Reconciled"/> marks a request closed against an existing user.</summary>
    private sealed record Approval(EntraAccessRequest? Request, User? User, string? DisplayName, bool RoleAssigned)
    {
        public static readonly Approval Reconciled = new(null, null, null, false);
    }

    private static AuditedOperation Operation(string primaryAction) => new(primaryAction, AuditActor.CurrentPrincipal);

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Provisioning requires the audited unit of work.");

    private IAuditEventWriter Events =>
        _auditEvents ?? throw new InvalidOperationException("Provisioning requires the audit event writer.");

    private async ValueTask<string?> ReconcilePendingAsync(
        IAuditedTransaction transaction,
        string tenantId,
        string objectId,
        UserId userId,
        ActorId actorId,
        DateTimeOffset reviewedAt,
        CancellationToken cancellationToken)
    {
        var pending = await _requests.FindPendingAsync(tenantId, objectId, cancellationToken);
        if (pending is null)
            return null;

        await _requests.UpdateAsync(
            pending with
            {
                Status = EntraAccessRequestStatus.Approved,
                ReviewedAtUtc = reviewedAt,
                ReviewedByUserId = actorId.Value,
                ProvisionedUserId = userId.Value
            },
            cancellationToken);
        await transaction.StageAsync(AuditEvents.AccessRequestReconciled(pending.Id, tenantId, objectId, userId), cancellationToken);
        return pending.Id;
    }

    /// <summary>
    /// Sends the welcome mail once, after the user committed, and records the send as a W3 row. A failed or throwing send
    /// is reported in the result and never undoes or contradicts the committed user.
    /// </summary>
    private async ValueTask<(bool Attempted, bool Delivered, string? Failure)> TrySendWelcomeAsync(
        UserId userId,
        bool requested,
        string? graphMail,
        string displayName,
        string? registrationDisplayName,
        CancellationToken cancellationToken)
    {
        if (!requested)
            return (false, false, null);

        var mail = ContactEmailAddress.Normalize(graphMail);
        if (mail is null)
            return (false, false, null);

        var signIn = _links.SignInWithMicrosoft();
        var org = string.IsNullOrWhiteSpace(registrationDisplayName) ? "this Apperture installation" : registrationDisplayName.Trim();
        var body =
            $"Hello {displayName},\n\n" +
            $"An administrator has given you access to Apperture for {org}.\n\n" +
            "Sign in with Microsoft. You do not need to set an Apperture password.\n\n" +
            signIn;

        AccountSecurityMailResult delivery;
        string outcome;
        string? failure;
        try
        {
            delivery = await _mail.SendAsync(mail, "You have access to Apperture", body, cancellationToken);
            outcome = delivery.Succeeded ? AuditOutcomeCodes.Succeeded : AuditOutcomeCodes.Failed;
            failure = delivery.Succeeded
                ? null
                : string.Equals(delivery.FailureSummary, AccountSecurityErrorCodes.SmtpNotConfigured, StringComparison.Ordinal)
                    ? "not_configured"
                    : "send_failed";
        }
        catch (Exception)
        {
            delivery = new AccountSecurityMailResult(false, AccountSecurityMailDelivery.DeliveryUnknown);
            outcome = AuditOutcomeCodes.Unknown;
            failure = "error";
        }

        await Events.TryWriteAsync(AuditEvents.Welcome(userId, outcome, failure), AuditActor.CurrentPrincipal, CancellationToken.None);
        return (true, delivery.Succeeded, delivery.FailureSummary);
    }

    /// <summary>Assigns the initial role inside the provisioning transaction, with its own assignment row.</summary>
    private async ValueTask<bool> TryAssignRoleAsync(IAuditedTransaction transaction, UserId userId, string? roleId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleId))
            return false;
        var id = new RoleId(roleId.Trim());
        var role = await _roles.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Role {roleId} was not found.");
        await _roles.AssignToUserAsync(userId, role.Id, cancellationToken);
        await transaction.StageAsync(AuditEvents.RoleAssigned(userId, role.Id), cancellationToken);
        return true;
    }

    private async ValueTask RequireRoleAssignIfRequestedAsync(string? roleId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleId))
            return;
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.RoleAssign, new ResourceDescriptor(ResourceTypeConstants.Role, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.RoleAssignForbidden,
                "You can add people but you cannot assign that role.");
        }
    }

    private async ValueTask<ActorId> RequireUserManageAsync(CancellationToken cancellationToken)
    {
        var actor = await _principalContext.GetCurrentActorAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("No authenticated actor.");
        var allowed = await _auth.EvaluateAsync(
            actor,
            new PermissionDescriptor(ActionConstants.UserManage, new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance)),
            null, null, cancellationToken);
        if (!allowed)
            throw new UnauthorizedAccessException("Not authorized to manage users.");
        return new ActorId(actor.Id.Value);
    }

    /// <summary>
    /// Persists a new Entra user under a generated username (UPN, mail, display name, then <c>user</c>;
    /// never the object id). A failed add reserves no name. Returns the user exactly as persisted, so callers
    /// report its stored username rather than recomputing one.
    /// </summary>
    private async ValueTask<User> AddWithGeneratedUsernameAsync(
        Func<string, User> buildUser,
        UsernameGenerationSources sources,
        CancellationToken cancellationToken)
    {
        try
        {
            return await new UsernameService(_users).AddWithGeneratedUsernameAsync(buildUser, sources, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.DuplicateIdentity,
                "That Microsoft identity is already an Apperture user.");
        }
        catch (UsernameException ex) when (ex.Code == UsernameErrorCodes.GenerationExhausted)
        {
            throw new UserProvisioningException(UserProvisioningErrorCodes.UsernameGenerationExhausted, ex.Message);
        }
    }

    private static UserId NewUserId() => new(Guid.NewGuid().ToString("N")[..24]);
}
