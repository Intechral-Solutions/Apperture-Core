using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Identity;
using Apperture.Core.Security.Secrets.Abstractions;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.Configuration;

namespace Apperture.ControlPlane.Security;

public sealed class EntraAccessRequestService : IEntraAccessRequestService
{
    internal static readonly TimeSpan RejectionCooldown = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);
    internal const int RateLimitMax = 5;

    private readonly IPrincipalContext _principalContext;
    private readonly IAuthorizationService _auth;
    private readonly IUserRepository _users;
    private readonly IEntraInstanceRegistrationRepository _registrations;
    private readonly IEntraDirectoryDiscoveryService _discovery;
    private readonly IEntraAccessRequestRepository _requests;
    private readonly IUserProvisioningService _provisioning;
    private readonly ISecretResolver? _secrets;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;
    private readonly IAuditedUnitOfWork? _auditedUnitOfWork;

    public EntraAccessRequestService(
        IPrincipalContext principalContext,
        IAuthorizationService auth,
        IUserRepository users,
        IEntraInstanceRegistrationRepository registrations,
        IEntraDirectoryDiscoveryService discovery,
        IEntraAccessRequestRepository requests,
        IUserProvisioningService provisioning,
        IConfiguration configuration,
        TimeProvider clock,
        ISecretResolver? secrets = null,
        IAuditedUnitOfWork? auditedUnitOfWork = null)
    {
        _principalContext = principalContext;
        _auth = auth;
        _users = users;
        _registrations = registrations;
        _discovery = discovery;
        _requests = requests;
        _provisioning = provisioning;
        _configuration = configuration;
        _clock = clock;
        _secrets = secrets;
        _auditedUnitOfWork = auditedUnitOfWork;
    }

    private IAuditedUnitOfWork Unit =>
        _auditedUnitOfWork ?? throw new InvalidOperationException("Access requests require the audited unit of work.");

    public async ValueTask<EntraAccessRequestSessionSnapshot> GetSessionSnapshotAsync(
        EntraAccessRequestPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var owner = await _users.GetByIdentityLinkAsync(
            ProviderIdentityKey.Entra(principal.TenantId, principal.ObjectId),
            cancellationToken);
        if (owner is not null)
        {
            return Snapshot(
                EntraAccessRequestSessionStates.AlreadyProvisioned,
                principal,
                null,
                false,
                "You already have access. Sign in with Microsoft again.");
        }

        var registration = await _registrations.GetByIdAsync(principal.RegistrationId, cancellationToken);
        if (registration is null || !registration.Enabled)
        {
            return Snapshot(
                EntraAccessRequestSessionStates.RegistrationDisabled,
                principal,
                registration,
                false,
                "This Microsoft directory is not accepting access requests.");
        }

        var pending = await _requests.FindPendingAsync(
            principal.TenantId,
            principal.ObjectId,
            cancellationToken);
        if (pending is not null)
        {
            return Snapshot(
                EntraAccessRequestSessionStates.Pending,
                principal,
                registration,
                false,
                "An Apperture administrator needs to approve your request.",
                pending.Id,
                pending.RequestedAtUtc);
        }

        var latest = await _requests.FindLatestAsync(
            principal.TenantId,
            principal.ObjectId,
            cancellationToken);
        if (latest?.Status == EntraAccessRequestStatus.Approved && !string.IsNullOrEmpty(latest.ProvisionedUserId))
        {
            return Snapshot(
                EntraAccessRequestSessionStates.Approved,
                principal,
                registration,
                false,
                "Your request was approved. Sign in with Microsoft to continue.");
        }

        var directory = await TryGetDirectoryUserAsync(registration, principal.ObjectId, cancellationToken);
        if (directory is null)
        {
            return Snapshot(
                EntraAccessRequestSessionStates.EligibilityUnavailable,
                principal,
                registration,
                false,
                "Your organization could not confirm this Microsoft identity right now. Try again later.");
        }

        var isGuest = IsGuest(directory.UserType);
        if (!registration.AllowMemberAccessRequests && !registration.AllowGuestAccessRequests)
        {
            return Snapshot(
                EntraAccessRequestSessionStates.RequestsDisabled,
                principal,
                registration,
                false,
                "Your organization has not enabled access requests.");
        }

        if (isGuest && !registration.AllowGuestAccessRequests)
        {
            return Snapshot(
                EntraAccessRequestSessionStates.GuestDisabled,
                principal,
                registration,
                false,
                "Guest Microsoft identities cannot request access.");
        }

        if (!isGuest && !registration.AllowMemberAccessRequests)
        {
            return Snapshot(
                EntraAccessRequestSessionStates.RequestsDisabled,
                principal,
                registration,
                false,
                "Your organization has not enabled access requests.");
        }

        if (latest?.Status == EntraAccessRequestStatus.Rejected)
        {
            var retryAt = latest.ReviewedAtUtc?.Add(RejectionCooldown) ?? latest.RequestedAtUtc.Add(RejectionCooldown);
            if (_clock.GetUtcNow() < retryAt)
            {
                return Snapshot(
                    EntraAccessRequestSessionStates.Rejected,
                    principal,
                    registration,
                    false,
                    "Your request was not approved.");
            }
        }

        return Snapshot(
            EntraAccessRequestSessionStates.Eligible,
            principal,
            registration,
            true,
            isGuest
                ? "Your organization allows guest identities to request access."
                : "Your organization allows members to request access.",
            userType: directory.UserType);
    }

    public async ValueTask<EntraAccessRequest> SubmitAsync(
        EntraAccessRequestPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSessionSnapshotAsync(principal, cancellationToken);
        if (snapshot.State == EntraAccessRequestSessionStates.Pending && snapshot.PendingRequestId is not null)
        {
            var existing = await _requests.GetByIdAsync(snapshot.PendingRequestId, cancellationToken);
            if (existing is not null)
                return existing;
        }

        if (!snapshot.CanRequest)
        {
            throw new EntraAccessRequestException(
                snapshot.State == EntraAccessRequestSessionStates.Throttled
                    ? EntraAccessRequestErrorCodes.Throttled
                    : EntraAccessRequestErrorCodes.NotEligible,
                snapshot.Message ?? "You cannot request access right now.");
        }

        var recent = await _requests.CountRecentForIdentityAsync(
            principal.TenantId,
            principal.ObjectId,
            _clock.GetUtcNow() - RateLimitWindow,
            cancellationToken);
        if (recent >= RateLimitMax)
        {
            throw new EntraAccessRequestException(
                EntraAccessRequestErrorCodes.Throttled,
                "You cannot request access right now.");
        }

        // A directory lookup is an external call: it runs before the transaction, never inside it.
        var directory = await TryGetDirectoryUserAsync(
            await _registrations.GetByIdAsync(principal.RegistrationId, cancellationToken),
            principal.ObjectId,
            cancellationToken);

        var now = _clock.GetUtcNow();
        var request = new EntraAccessRequest(
            Guid.NewGuid().ToString("N")[..24],
            principal.RegistrationId,
            principal.TenantId,
            principal.ObjectId,
            FirstNonEmpty(directory?.DisplayName, principal.DisplayNameSnapshot),
            FirstNonEmpty(directory?.UserPrincipalName, principal.UserPrincipalNameSnapshot),
            FirstNonEmpty(directory?.Mail, principal.MailSnapshot),
            directory?.UserType,
            principal.AuthenticatedAtUtc,
            now,
            EntraAccessRequestStatus.Pending);

        // The requester has no Apperture user. The access-request session is issued only by the sign-in handler after it
        // validated the token and this instance's tenant check passed, in a data-protected cookie of its own scheme, so its
        // tenant id and object id are a verified external identity (D1, clarification 2).
        var requester = AuditActor.ValidatedExternalIdentity(
            new AuditExternalActor(AuditExternalActor.EntraProvider, principal.TenantId, principal.ObjectId));

        try
        {
            return await Unit.ExecuteAsync(
                new AuditedOperation(AuditActionNames.AccessRequestCreated, requester),
                async (transaction, ct) =>
                {
                    // The identity, the registration and its access-request policy, pending state, rejection cooldown and rate
                    // limit are read again in the transaction: an administrator may have changed them since the snapshot. Only
                    // the directory's member or guest type (an external read) comes from before it.
                    if (await _users.GetByIdentityLinkAsync(ProviderIdentityKey.Entra(principal.TenantId, principal.ObjectId), ct) is not null)
                        throw new EntraAccessRequestException(EntraAccessRequestErrorCodes.NotEligible, "You already have access. Sign in with Microsoft again.");

                    var registration = await _registrations.GetByIdAsync(principal.RegistrationId, ct);
                    if (registration is null || !registration.Enabled)
                        throw new EntraAccessRequestException(EntraAccessRequestErrorCodes.NotEligible, "This Microsoft directory is not accepting access requests.");
                    var isGuest = IsGuest(directory?.UserType ?? snapshot.UserType);
                    if (isGuest ? !registration.AllowGuestAccessRequests : !registration.AllowMemberAccessRequests)
                        throw new EntraAccessRequestException(EntraAccessRequestErrorCodes.NotEligible, "Your organization has not enabled access requests.");

                    var latest = await _requests.FindLatestAsync(principal.TenantId, principal.ObjectId, ct);
                    if (latest?.Status == EntraAccessRequestStatus.Rejected
                        && now < (latest.ReviewedAtUtc ?? latest.RequestedAtUtc).Add(RejectionCooldown))
                        throw new EntraAccessRequestException(EntraAccessRequestErrorCodes.NotEligible, "Your request was not approved.");

                    var pending = await _requests.FindPendingAsync(principal.TenantId, principal.ObjectId, ct);
                    if (pending is not null)
                    {
                        transaction.DeclareNoOp();
                        return pending;
                    }

                    if (await _requests.CountRecentForIdentityAsync(principal.TenantId, principal.ObjectId, now - RateLimitWindow, ct) >= RateLimitMax)
                        throw new EntraAccessRequestException(EntraAccessRequestErrorCodes.Throttled, "You cannot request access right now.");

                    await _requests.AddAsync(request, ct);
                    await transaction.StageAsync(
                        AuditEvents.AccessRequestCreated(request.Id, request.RegistrationId, request.TenantId, request.ProviderObjectId), ct);
                    return request;
                },
                (_, _) => Task.FromResult(request),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not EntraAccessRequestException
            && ex is not AuditOutcomeUnknownException
            && ex is not AuditOperationNotCompletedException)
        {
            // Another submission for the same identity committed first (the pending-request index refused this insert).
            var raced = await _requests.FindPendingAsync(principal.TenantId, principal.ObjectId, cancellationToken);
            if (raced is not null)
                return raced;
            throw;
        }
    }

    public async ValueTask<(IReadOnlyList<EntraAccessRequest> Items, int PendingCount)> ListPendingForReviewAsync(
        CancellationToken cancellationToken = default)
    {
        await RequireUserManageAsync(cancellationToken);
        var items = await _requests.ListPendingAsync(cancellationToken);
        return (items, items.Count);
    }

    public async ValueTask<UserProvisioningResult> ApproveAsync(
        string requestId,
        string? initialRoleId,
        bool sendWelcomeEmail,
        CancellationToken cancellationToken = default)
    {
        return await _provisioning.ProvisionFromAccessRequestAsync(
            new AccessRequestApprovalProvisioningRequest(requestId, initialRoleId, sendWelcomeEmail),
            cancellationToken);
    }

    public async ValueTask RejectAsync(string requestId, CancellationToken cancellationToken = default)
    {
        var actorId = await RequireUserManageAsync(cancellationToken);
        var reviewedAt = _clock.GetUtcNow();

        // The administrator's decision: the pending check, the update and the row commit together.
        await Unit.ExecuteAsync(
            new AuditedOperation(AuditActionNames.AccessRequestRejected, AuditActor.CurrentPrincipal),
            async (transaction, ct) =>
            {
                var request = await _requests.GetByIdAsync(requestId, ct)
                    ?? throw new EntraAccessRequestException(EntraAccessRequestErrorCodes.NotFound, "That access request was not found.");
                if (request.Status != EntraAccessRequestStatus.Pending)
                    throw new EntraAccessRequestException(EntraAccessRequestErrorCodes.NotPending, "That access request is no longer pending.");

                await _requests.UpdateAsync(
                    request with
                    {
                        Status = EntraAccessRequestStatus.Rejected,
                        ReviewedAtUtc = reviewedAt,
                        ReviewedByUserId = actorId.Value
                    },
                    ct);
                await transaction.StageAsync(AuditEvents.AccessRequestRejected(request.Id, request.TenantId, request.ProviderObjectId), ct);
                return true;
            },
            (_, _) => Task.FromResult(true),
            cancellationToken);
    }

    private async ValueTask<EntraDirectoryUser?> TryGetDirectoryUserAsync(
        EntraInstanceRegistration? registration,
        string objectId,
        CancellationToken cancellationToken)
    {
        if (registration is null)
            return null;
        try
        {
            var secret = await EntraClientSecretAccess.TryResolveAsync(
                registration,
                _secrets,
                _configuration[EntraAuthOptions.SectionName + ":" + nameof(EntraAuthOptions.ClientSecret)],
                cancellationToken) ?? "";
            var tenantId = EntraDirectoryTenantResolver.TryResolveGraphTenantId(registration) ?? "";
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(tenantId))
                return null;
            var result = await _discovery.GetUserByIdAsync(
                new EntraDirectoryDiscoveryContext(tenantId, registration.ClientId, secret, registration.Id),
                objectId,
                cancellationToken);
            return result.Success ? result.Data : null;
        }
        catch
        {
            return null;
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

    private static bool IsGuest(string? userType) =>
        string.Equals(userType, "Guest", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static EntraAccessRequestSessionSnapshot Snapshot(
        string state,
        EntraAccessRequestPrincipal principal,
        EntraInstanceRegistration? registration,
        bool canRequest,
        string message,
        string? pendingRequestId = null,
        DateTimeOffset? requestedAtUtc = null,
        string? userType = null) =>
        new(
            state,
            principal.DisplayNameSnapshot,
            FirstNonEmpty(principal.MailSnapshot, principal.UserPrincipalNameSnapshot),
            userType,
            registration?.DisplayName,
            canRequest,
            message,
            pendingRequestId,
            requestedAtUtc);
}
