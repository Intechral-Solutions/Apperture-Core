namespace Apperture.Core.Application.Services;

/// <summary>
/// Expected failure when an operation would destroy the instance emergency-access recovery path.
/// </summary>
public sealed class EmergencyAccessException : Exception
{
    public EmergencyAccessException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable codes for APIs, CLI, and the management UI.</summary>
public static class EmergencyAccessErrorCodes
{
    public const string NotPlatformAdministrator = "emergency.access.not_platform_administrator";
    public const string LocalCredentialRequired = "emergency.access.local_credential_required";
    public const string UserNotFound = "emergency.access.user_not_found";
    public const string AlreadyDesignated = "emergency.access.already_designated";
    public const string NotDesignated = "emergency.access.not_designated";
    public const string ConfirmFallbackRequired = "emergency.access.confirm_fallback_required";
    public const string UnsafeOperation = "emergency.access.unsafe_operation";
    public const string DuplicateUsername = "emergency.access.duplicate_username";
    public const string RoleNotFound = "emergency.access.role_not_found";
    public const string ReservedUsername = "emergency.access.reserved_username";
    public const string UsernameConflict = "emergency.access.username_conflict";
}
