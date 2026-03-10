namespace Apperture.Core.Security.Trust;

/// <summary>
/// Manages enrollment invitations and runtime identity registration.
/// Enrollment uses short-lived tokens; operational auth uses the registered identity after enrollment.
/// </summary>
public interface IRuntimeEnrollmentService
{
    /// <summary>
    /// Creates an enrollment invitation for a runtime. Returns a short-lived token (e.g. 24h) and enrollment URL.
    /// </summary>
    ValueTask<EnrollmentInvitation> CreateInvitationAsync(
        string runtimeId,
        TimeSpan? ttl = null,
        IReadOnlySet<string>? scopes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an enrollment token without consuming it.
    /// </summary>
    ValueTask<EnrollmentTokenValidationResult> ValidateTokenAsync(
        string enrollmentToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an enrollment token, marks it used, and registers the runtime identity.
    /// </summary>
    ValueTask<EnrollmentResult> EnrollAsync(
        string enrollmentToken,
        RuntimeIdentityRegistration registration,
        CancellationToken cancellationToken = default);
}
