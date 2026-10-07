using Apperture.Core.Application.Audit;
using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>Native TOTP MFA and recovery codes for emergency (and future local) authentication.</summary>
/// <remarks>
/// AUD-1.2: each change commits with its audit row. The administrative verbs take who runs them (the emergency command line
/// passes <see cref="AuditActor.EmergencyCli"/>); the affected user is the target, never the actor. Recovery codes are never
/// recorded, only how many were issued.
/// </remarks>
public interface INativeMfaService
{
    ValueTask<bool> IsEnrolledAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>True when this user must complete native MFA after a successful local password.</summary>
    ValueTask<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default);

    ValueTask<NativeMfaEnrollmentChallenge> BeginEnrollmentAsync(UserId userId, string accountName, CancellationToken cancellationToken = default);

    /// <summary>Verifies the first TOTP code, marks MFA enrolled, and returns one-time recovery codes.</summary>
    ValueTask<IReadOnlyList<string>> CompleteEnrollmentAsync(UserId userId, string totpCode, AuditActor actor, CancellationToken cancellationToken = default);

    ValueTask<bool> VerifyTotpAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes one unused recovery code of <paramref name="userId"/> during that user's sign-in; the user being
    /// authenticated is the actor. False, with nothing recorded, when the code does not match.
    /// </summary>
    ValueTask<bool> ConsumeRecoveryCodeAsync(UserId userId, string recoveryCode, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default);

    ValueTask ResetAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default);

    ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Protected pending-login ticket issued after password success when MFA is required. It identifies the user by
/// user id only; the session issued after the challenge is built from the user record.
/// </summary>
public interface IPendingMfaLoginTicketService
{
    ValueTask<string> IssueAsync(UserId userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    ValueTask<PendingMfaLoginTicket?> RedeemAsync(string token, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
}

public sealed record PendingMfaLoginTicket(UserId UserId, DateTimeOffset ExpiresUtc);
