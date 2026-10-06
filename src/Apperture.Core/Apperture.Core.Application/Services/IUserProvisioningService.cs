using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Canonical Apperture user-creation boundary. Authentication identities are attached here;
/// Microsoft authentication itself never creates a user.
/// </summary>
public interface IUserProvisioningService
{
    /// <summary>
    /// Creates a platform user from a Graph-backed Entra directory object. No local identity,
    /// credential, or setup invitation is created. The Entra link is pending until first sign-in.
    /// </summary>
    ValueTask<UserProvisioningResult> ProvisionFromEntraDirectoryAsync(
        EntraDirectoryProvisioningRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a platform user from an approved access request. The Entra link is proven using the
    /// request's authenticated-at timestamp (the identity already completed Apperture's OIDC pipeline).
    /// </summary>
    ValueTask<UserProvisioningResult> ProvisionFromAccessRequestAsync(
        AccessRequestApprovalProvisioningRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record EntraDirectoryProvisioningRequest(
    string RegistrationId,
    string ObjectId,
    string? InitialRoleId = null,
    bool SendWelcomeEmail = false);

public sealed record AccessRequestApprovalProvisioningRequest(
    string RequestId,
    string? InitialRoleId = null,
    bool SendWelcomeEmail = false);

/// <summary>
/// The outcome of a successful provisioning. <paramref name="Username"/> is the canonical username persisted for
/// the new user (the stored value, not a recomputation); failures throw and carry no result.
/// </summary>
public sealed record UserProvisioningResult(
    UserId UserId,
    string Username,
    string DisplayName,
    string ObjectId,
    bool EntraLinkProven,
    bool WelcomeMailAttempted,
    bool WelcomeMailDelivered,
    string? WelcomeMailFailureSummary,
    bool RoleAssigned,
    string? ReconciledRequestId);

public sealed class UserProvisioningException : InvalidOperationException
{
    public UserProvisioningException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class UserProvisioningErrorCodes
{
    public const string DuplicateIdentity = "user.provisioning.duplicate_identity";
    public const string DirectoryUserDisabled = "user.provisioning.directory_user_disabled";
    public const string DirectoryUserMissing = "user.provisioning.directory_user_missing";
    public const string RegistrationNotFound = "user.provisioning.registration_not_found";
    public const string RegistrationDisabled = "user.provisioning.registration_disabled";
    public const string InvalidObjectId = "user.provisioning.invalid_object_id";
    public const string RoleAssignForbidden = "user.provisioning.role_assign_forbidden";
    public const string RequestNotPending = "user.provisioning.request_not_pending";
    public const string RequestNotFound = "user.provisioning.request_not_found";
    public const string DisplayNameRequired = "user.provisioning.display_name_required";
    public const string UsernameGenerationExhausted = UsernameErrorCodes.GenerationExhausted;
}

public static class UserProvisioningAuditActionNames
{
    public const string EntraDirectoryProvisioned = "user.provisioned.entra_directory";
    public const string AccessRequestProvisioned = "user.provisioned.entra_access_request";
    public const string WelcomeSent = "user.provisioning.welcome.sent";
    public const string WelcomeFailed = "user.provisioning.welcome.failed";
    public const string RequestReconciled = "entra.access_request.reconciled_by_provisioning";
    public const string Outcome = "auth-admin";
}
