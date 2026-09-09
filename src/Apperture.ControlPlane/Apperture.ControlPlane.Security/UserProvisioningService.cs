using Apperture.Core.Application.Accounts;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Audit;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

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
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly IAccountSecurityMailSender _mail;
    private readonly AccountSecurityLinkBuilder _links;

    public UserProvisioningService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IUserRepository users,
        IRoleRepository roles,
        IEntraInstanceRegistrationRepository registrations,
        IEntraProvisioningDirectoryService directory,
        IEntraIdentityAssociationService association,
        IEntraAccessRequestRepository requests,
        IAuditService audit,
        TimeProvider clock,
        IAccountSecurityMailSender mail,
        AccountSecurityLinkBuilder links)
    {
        _principalContext = principalContext;
        _auth = auth;
        _users = users;
        _roles = roles;
        _registrations = registrations;
        _directory = directory;
        _association = association;
        _requests = requests;
        _audit = audit;
        _clock = clock;
        _mail = mail;
        _links = links;
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

        var existing = await _users.GetByIdentityLinkAsync(IdentitySource.Entra, objectId!, cancellationToken);
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

        var pendingLink = await _association.BuildPendingAdminLinkAsync(objectId!, registration, cancellationToken);
        var userId = NewUserId();
        var user = new User(
            userId,
            displayName,
            [pendingLink],
            [],
            IsBreakGlassEligible: false);

        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.DuplicateIdentity,
                "That Microsoft identity is already an Apperture user.");
        }

        var roleAssigned = await TryAssignRoleAsync(userId, request.InitialRoleId, cancellationToken);
        await AuditAsync(
            UserProvisioningAuditActionNames.EntraDirectoryProvisioned,
            actorId,
            userId.Value,
            $"userId={userId.Value} oid={objectId} registrationId={registration.Id} proven=0",
            cancellationToken);

        var welcome = await TrySendWelcomeAsync(
            actorId,
            userId,
            request.SendWelcomeEmail,
            directoryUser.Mail,
            displayName,
            registration.DisplayName,
            cancellationToken);

        var reconciled = await ReconcilePendingAsync(registration.Id, objectId!, userId, actorId, cancellationToken);

        return new UserProvisioningResult(
            userId,
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

        var accessRequest = await _requests.GetByIdAsync(request.RequestId, cancellationToken)
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
        var existing = await _users.GetByIdentityLinkAsync(IdentitySource.Entra, objectId!, cancellationToken);
        if (existing is not null)
        {
            var already = accessRequest with
            {
                Status = EntraAccessRequestStatus.Approved,
                ReviewedAtUtc = _clock.GetUtcNow(),
                ReviewedByUserId = actorId.Value,
                ProvisionedUserId = existing.Id.Value
            };
            await _requests.UpdateAsync(already, cancellationToken);
            await AuditAsync(
                UserProvisioningAuditActionNames.RequestReconciled,
                actorId,
                existing.Id.Value,
                $"requestId={accessRequest.Id} userId={existing.Id.Value} oid={objectId}",
                cancellationToken);
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.DuplicateIdentity,
                "That Microsoft identity is already an Apperture user.");
        }

        var displayName = string.IsNullOrWhiteSpace(accessRequest.DisplayNameSnapshot)
            ? "Microsoft user"
            : accessRequest.DisplayNameSnapshot.Trim();

        var provenAt = accessRequest.AuthenticatedAtUtc;
        var link = new IdentityLink(
            IdentitySource.Entra,
            objectId!,
            LastAuthenticatedAt: provenAt,
            TenantId: accessRequest.TenantId,
            RegistrationId: accessRequest.RegistrationId,
            LinkedAt: _clock.GetUtcNow(),
            ProviderSubject: null,
            DisplayNameSnapshot: accessRequest.DisplayNameSnapshot,
            UserPrincipalNameSnapshot: accessRequest.UserPrincipalNameSnapshot,
            MailSnapshot: accessRequest.MailSnapshot);

        var userId = NewUserId();
        var user = new User(userId, displayName, [link], [], IsBreakGlassEligible: false);
        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserProvisioningException(
                UserProvisioningErrorCodes.DuplicateIdentity,
                "That Microsoft identity is already an Apperture user.");
        }

        var roleAssigned = await TryAssignRoleAsync(userId, request.InitialRoleId, cancellationToken);
        var approved = accessRequest with
        {
            Status = EntraAccessRequestStatus.Approved,
            ReviewedAtUtc = _clock.GetUtcNow(),
            ReviewedByUserId = actorId.Value,
            ProvisionedUserId = userId.Value
        };
        await _requests.UpdateAsync(approved, cancellationToken);

        await AuditAsync(
            UserProvisioningAuditActionNames.AccessRequestProvisioned,
            actorId,
            userId.Value,
            $"userId={userId.Value} oid={objectId} requestId={accessRequest.Id} proven=1 provenAt={provenAt:O}",
            cancellationToken);
        await AuditAsync(
            EntraAccessRequestAuditActionNames.Approved,
            actorId,
            userId.Value,
            $"requestId={accessRequest.Id} userId={userId.Value} oid={objectId}",
            cancellationToken);

        var registration = await _registrations.GetByIdAsync(accessRequest.RegistrationId, cancellationToken);
        var welcome = await TrySendWelcomeAsync(
            actorId,
            userId,
            request.SendWelcomeEmail,
            accessRequest.MailSnapshot,
            displayName,
            registration?.DisplayName,
            cancellationToken);

        return new UserProvisioningResult(
            userId,
            displayName,
            objectId!,
            EntraLinkProven: true,
            welcome.Attempted,
            welcome.Delivered,
            welcome.Failure,
            roleAssigned,
            accessRequest.Id);
    }

    private async ValueTask<string?> ReconcilePendingAsync(
        string registrationId,
        string objectId,
        UserId userId,
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var registration = await _registrations.GetByIdAsync(registrationId, cancellationToken);
        var tenantId = registration is null
            ? null
            : EntraTokenClaims.Normalize(EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration));
        if (string.IsNullOrEmpty(tenantId))
            return null;

        var pending = await _requests.FindPendingAsync(registrationId, tenantId, objectId, cancellationToken);
        if (pending is null)
            return null;

        var updated = pending with
        {
            Status = EntraAccessRequestStatus.Approved,
            ReviewedAtUtc = _clock.GetUtcNow(),
            ReviewedByUserId = actorId.Value,
            ProvisionedUserId = userId.Value
        };
        await _requests.UpdateAsync(updated, cancellationToken);
        await AuditAsync(
            UserProvisioningAuditActionNames.RequestReconciled,
            actorId,
            userId.Value,
            $"requestId={pending.Id} userId={userId.Value} oid={objectId}",
            cancellationToken);
        return pending.Id;
    }

    private async ValueTask<(bool Attempted, bool Delivered, string? Failure)> TrySendWelcomeAsync(
        ActorId actorId,
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

        var delivery = await _mail.SendAsync(mail, "You have access to Apperture", body, cancellationToken);
        if (delivery.Succeeded)
        {
            await AuditAsync(
                UserProvisioningAuditActionNames.WelcomeSent,
                actorId,
                userId.Value,
                $"userId={userId.Value} delivered=1",
                cancellationToken);
            return (true, true, null);
        }

        await AuditAsync(
            UserProvisioningAuditActionNames.WelcomeFailed,
            actorId,
            userId.Value,
            $"userId={userId.Value} purpose=welcome",
            cancellationToken);
        return (true, false, delivery.FailureSummary);
    }

    private async ValueTask<bool> TryAssignRoleAsync(UserId userId, string? roleId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleId))
            return false;
        var id = new RoleId(roleId.Trim());
        var role = await _roles.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Role {roleId} was not found.");
        await _roles.AssignToUserAsync(userId, role.Id, cancellationToken);
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

    private async ValueTask AuditAsync(string action, ActorId actorId, string resourceId, string details, CancellationToken cancellationToken)
    {
        await _audit.AppendAsync(
            new AuditRecord(
                new AuditRecordId(Guid.NewGuid().ToString("N")),
                actorId,
                action,
                new ResourceDescriptor(ResourceTypeConstants.User, ScopeKeyConstants.Instance),
                UserProvisioningAuditActionNames.Outcome,
                _clock.GetUtcNow(),
                new CorrelationId(null),
                details),
            cancellationToken);
        _ = resourceId;
    }

    private static UserId NewUserId() => new(Guid.NewGuid().ToString("N")[..24]);
}
