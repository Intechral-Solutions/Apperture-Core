namespace Apperture.ControlPlane.Security;

/// <summary>Audit action names for emergency access and native MFA (auth category).</summary>
public static class EmergencyAccessAuditActionNames
{
    public const string Designated = "emergency.access.designated";
    public const string Provisioned = "emergency.access.provisioned";
    public const string Revoked = "emergency.access.revoked";
    public const string Canonicalized = "emergency.access.canonicalized";
    public const string LocalProtected = "emergency.local.protected";
    public const string BlockedUnsafe = "emergency.auth.blocked.unsafe_operation";
    public const string LoginSuccess = "auth.emergency.login.success";
    public const string LoginFailed = "auth.emergency.login.failed";
    public const string MfaEnrolled = "auth.mfa.enrolled";
    public const string MfaReset = "auth.mfa.reset";
    public const string RecoveryCodesGenerated = "auth.mfa.recovery_codes.generated";
    public const string RecoveryCodeConsumed = "auth.mfa.recovery_code.consumed";
}
