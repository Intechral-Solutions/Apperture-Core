using Apperture.Shared.Kernel;

namespace Apperture.ControlPlane.Security;

// The typed outcomes of governance operations. None derives from InvalidOperationException: a business outcome is never read
// from that type or from a message, and an InvalidOperationException that reaches a host is an internal error (500).

/// <summary>
/// The actor is authenticated and may use the operation, but not for what was asked: the request would give someone more
/// authority than the actor holds, or touches a role only a Platform Administrator may manage (403).
/// </summary>
public sealed class AuthorizationRefusalException : Exception
{
    /// <summary>The most missing permission identities a refusal carries.</summary>
    public const int MaxMissingReported = 20;

    public AuthorizationRefusalException(string code, string message, IReadOnlyList<PermissionDescriptor>? missing = null)
        : base(message)
    {
        Code = code;
        MissingTotal = missing?.Count ?? 0;
        Missing = missing is null
            ? []
            : missing.Select(Identity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxMissingReported).ToList();
    }

    public string Code { get; }

    /// <summary>The permissions the actor does not hold, as <c>action@resourceType:scopeKey</c>, at most <see cref="MaxMissingReported"/>.</summary>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>How many permissions the actor does not hold, whether or not all are listed.</summary>
    public int MissingTotal { get; }

    private static string Identity(PermissionDescriptor permission) =>
        permission.Resource is null
            ? permission.Action
            : $"{permission.Action}@{permission.Resource.ResourceType}:{permission.Resource.ScopeKey}";
}

/// <summary>Stable codes of <see cref="AuthorizationRefusalException"/>.</summary>
public static class AuthorizationRefusalCodes
{
    public const string GrantExceedsAuthority = "grant_exceeds_authority";
    public const string AssignmentExceedsAuthority = "assignment_exceeds_authority";
    public const string RemovalExceedsAuthority = "removal_exceeds_authority";
    public const string PlatformAdministratorRestricted = "platform_administrator_restricted";
}

/// <summary>The request conflicts with the state of the system (409): a built-in role, a name in use, a role that cannot be assigned.</summary>
public class GovernanceConflictException : Exception
{
    public GovernanceConflictException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>A conflict that a lower layer reported in its own terms (a concurrency failure of a write), kept as the cause.</summary>
    public GovernanceConflictException(string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable codes of <see cref="GovernanceConflictException"/> that are not role-lifecycle codes.</summary>
public static class GovernanceConflictCodes
{
    public const string RolePermissionsUnreadable = "role_permissions_unreadable";
    public const string LastViableAdministrator = "last_viable_administrator";

    /// <summary>I-VIABLE, for a link whose removal would leave no ordinary administrator able to sign in.</summary>
    public const string LastViableSignInMethod = "last_viable_sign_in_method";

    /// <summary>I-VIABLE, for a Local credential whose disablement would leave no ordinary administrator able to sign in.</summary>
    public const string LastLocalCredential = "last_local_credential";

    /// <summary>I-FALLBACK (and C-13-R): the change would leave no ordinary administrator who can sign in without Entra while Recovery Access cannot stand in.</summary>
    public const string EntraIndependentFallbackRequired = "entra_independent_fallback_required";

    /// <summary>
    /// The installation's authentication configuration could not be established, so a change that could reduce administrator
    /// viability is refused rather than guessed at.
    /// </summary>
    public const string AdministratorViabilityUnavailable = "administrator_viability_unavailable";

    /// <summary>
    /// Replacing an authenticator would have left the Recovery Access posture worse than it was. A working replacement never does this,
    /// so nothing was changed and the active authenticator stays in place.
    /// </summary>
    public const string RecoveryAccessPostureReduced = "recovery_access_posture_reduced";

    /// <summary>
    /// An authenticator replacement was not judged on the Recovery Access posture alone, because administrator access changed while it
    /// ran (another change moved an administrator's sign-in facts between the state before and the state after). Nothing was changed and
    /// the active authenticator stays in place; running the replacement again judges it afresh. Retryable.
    /// </summary>
    public const string AdministratorAccessChanged = "administrator_access_changed";

    /// <summary>The account's authenticator changed while this request ran (another change committed first), so nothing was changed. Retryable.</summary>
    public const string AuthenticatorChanged = "authenticator_changed";

    /// <summary>The account has no enrolled authenticator, so there is nothing to replace; nothing was changed. Enroll one instead.</summary>
    public const string AuthenticatorNotEnrolled = "authenticator_not_enrolled";

    /// <summary>
    /// The replacement named is no longer one that can complete: a newer one was started, it was already used, or it changed while the code
    /// was being checked. Nothing was changed; start the replacement again.
    /// </summary>
    public const string AuthenticatorReplacementNoLongerValid = "authenticator_replacement_no_longer_valid";

    /// <summary>The replacement expired before it completed, so nothing was changed and it was removed; start the replacement again.</summary>
    public const string AuthenticatorReplacementExpired = "authenticator_replacement_expired";
}

/// <summary>
/// A serialization wait expired (409, retry). No operation throws it yet: it is the transient-contention outcome the
/// administrator-set gate reports.
/// </summary>
public sealed class GovernanceBusyException : Exception
{
    public const string Code = "governance_operation_busy";

    public GovernanceBusyException()
        : base("Another change to administrator access is in progress. Try again in a moment.")
    {
    }
}

/// <summary>The user or role the request names does not exist (404).</summary>
public sealed class GovernanceNotFoundException : Exception
{
    public GovernanceNotFoundException(string kind, string id)
        : base($"{(kind == GovernanceNotFoundKinds.User ? "User" : "Role")} {id} was not found.")
    {
        Kind = kind;
    }

    /// <summary>What was not found: <see cref="GovernanceNotFoundKinds.User"/> or <see cref="GovernanceNotFoundKinds.Role"/>.</summary>
    public string Kind { get; }

    public string Code => Kind + "_not_found";
}

public static class GovernanceNotFoundKinds
{
    public const string User = "user";
    public const string Role = "role";
}

/// <summary>The request is malformed: an unknown permission, a missing role id, an empty name (400).</summary>
public sealed class GovernanceValidationException : Exception
{
    public GovernanceValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable codes of <see cref="GovernanceValidationException"/>.</summary>
public static class GovernanceValidationCodes
{
    public const string InvalidPermission = "invalid_permission";
    public const string RoleIdRequired = "role_id_required";
    public const string RoleNameRequired = "role_name_required";
    public const string RoleNameTooLong = "role_name_too_long";
    public const string RoleDescriptionTooLong = "role_description_too_long";
}
