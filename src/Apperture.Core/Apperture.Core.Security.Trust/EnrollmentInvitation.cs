namespace Apperture.Core.Security.Trust;

/// <summary>
/// Short-lived enrollment invitation returned from CreateInvitationAsync.
/// </summary>
public sealed record EnrollmentInvitation(
    string Token,
    string EnrollmentUrl,
    DateTimeOffset ExpiresAt);
