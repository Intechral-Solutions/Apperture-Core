using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Instance emergency-access posture. v1 allows exactly one designated emergency Platform Administrator.
/// </summary>
public sealed record EmergencyAccessPosture(
    bool IsHealthy,
    bool FallbackSafeguardRequired,
    string? DesignatedUserId,
    string? DesignatedDisplayName,
    bool DesignatedIsPlatformAdministrator,
    bool LocalCredentialEnabled,
    bool NativeMfaEnrolled,
    int UnusedRecoveryCodeCount);

/// <summary>
/// MFA enrollment material shown once. The secret is never persisted in this shape.
/// </summary>
public sealed record NativeMfaEnrollmentChallenge(
    UserId UserId,
    string OtpAuthUri,
    string ManualEntryKey,
    IReadOnlyList<string> RecoveryCodes);

/// <summary>Stored TOTP credential metadata. The protected secret is not included.</summary>
public sealed record NativeTotpCredentialStatus(
    UserId UserId,
    DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? LastVerifiedAtUtc);

/// <summary>Stored hashed recovery code. The plaintext code is not included.</summary>
public sealed record NativeMfaRecoveryCodeRecord(
    string Id,
    UserId UserId,
    string CodeHash,
    string Salt,
    string Algorithm,
    DateTimeOffset? ConsumedAtUtc);
