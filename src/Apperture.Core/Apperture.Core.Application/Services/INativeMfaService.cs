using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>Native TOTP MFA and recovery codes for emergency (and future local) authentication.</summary>
public interface INativeMfaService
{
    ValueTask<bool> IsEnrolledAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>True when this user must complete native MFA after a successful local password.</summary>
    ValueTask<bool> IsRequiredAsync(User user, CancellationToken cancellationToken = default);

    ValueTask<NativeMfaEnrollmentChallenge> BeginEnrollmentAsync(UserId userId, string accountName, CancellationToken cancellationToken = default);

    /// <summary>Verifies the first TOTP code, marks MFA enrolled, and returns one-time recovery codes.</summary>
    ValueTask<IReadOnlyList<string>> CompleteEnrollmentAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default);

    ValueTask<bool> VerifyTotpAsync(UserId userId, string totpCode, CancellationToken cancellationToken = default);

    ValueTask<bool> ConsumeRecoveryCodeAsync(UserId userId, string recoveryCode, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default);

    ValueTask ResetAsync(UserId userId, ActorId? actorId, CancellationToken cancellationToken = default);

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
