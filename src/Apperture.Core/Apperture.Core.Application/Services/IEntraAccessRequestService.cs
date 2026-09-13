using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>Self-service access requests and administrator review. Never creates a user on request.</summary>
public interface IEntraAccessRequestService
{
    ValueTask<EntraAccessRequestSessionSnapshot> GetSessionSnapshotAsync(
        EntraAccessRequestPrincipal principal,
        CancellationToken cancellationToken = default);

    ValueTask<EntraAccessRequest> SubmitAsync(
        EntraAccessRequestPrincipal principal,
        CancellationToken cancellationToken = default);

    ValueTask<(IReadOnlyList<EntraAccessRequest> Items, int PendingCount)> ListPendingForReviewAsync(
        CancellationToken cancellationToken = default);

    ValueTask<UserProvisioningResult> ApproveAsync(
        string requestId,
        string? initialRoleId,
        bool sendWelcomeEmail,
        CancellationToken cancellationToken = default);

    ValueTask RejectAsync(string requestId, CancellationToken cancellationToken = default);
}

/// <summary>Claims from the limited pre-provisioning cookie. Not an Apperture User.</summary>
public sealed record EntraAccessRequestPrincipal(
    string TenantId,
    string ObjectId,
    string RegistrationId,
    DateTimeOffset AuthenticatedAtUtc,
    string? DisplayNameSnapshot = null,
    string? UserPrincipalNameSnapshot = null,
    string? MailSnapshot = null);

public sealed record EntraAccessRequestSessionSnapshot(
    string State,
    string? DisplayName,
    string? DirectoryAddress,
    string? UserType,
    string? RegistrationDisplayName,
    bool CanRequest,
    string? Message,
    string? PendingRequestId,
    DateTimeOffset? RequestedAtUtc);

public static class EntraAccessRequestSessionStates
{
    public const string Eligible = "eligible";
    public const string Pending = "pending";
    public const string Rejected = "rejected";
    public const string Approved = "approved";
    public const string AlreadyProvisioned = "already_provisioned";
    public const string RequestsDisabled = "requests_disabled";
    public const string GuestDisabled = "guest_disabled";
    public const string EligibilityUnavailable = "eligibility_unavailable";
    public const string RegistrationDisabled = "registration_disabled";
    public const string Throttled = "throttled";
}

public static class EntraAccessRequestAuditActionNames
{
    public const string Created = "entra.access_request.created";
    public const string Approved = "entra.access_request.approved";
    public const string Rejected = "entra.access_request.rejected";
    public const string PolicyChanged = "entra.access_request.policy.changed";
    public const string Outcome = "auth-admin";
}

public sealed class EntraAccessRequestException : InvalidOperationException
{
    public EntraAccessRequestException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class EntraAccessRequestErrorCodes
{
    public const string NotEligible = "entra.access_request.not_eligible";
    public const string Throttled = "entra.access_request.throttled";
    public const string NotPending = "entra.access_request.not_pending";
    public const string NotFound = "entra.access_request.not_found";
}
