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

    /// <summary>
    /// The bare reset: removes the account's authenticator and recovery codes with nothing in their place. A reducer of administrator
    /// viability when it would take a healthy Recovery Access posture away; the replacement (<see cref="BeginReplacementAsync"/>) is the
    /// normal way to change an authenticator.
    /// </summary>
    ValueTask ResetAsync(UserId userId, AuditActor actor, CancellationToken cancellationToken = default);

    ValueTask<int> CountUnusedRecoveryCodesAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the non-destructive replacement of an enrolled authenticator: stages a candidate secret beside the active one and returns it
    /// once, for the operator to add to an authenticator app. Nothing about the active authenticator or its recovery codes changes, no
    /// lock is held across the operator's input, and an abandoned or expired candidate never disables the active authenticator.
    /// </summary>
    ValueTask<NativeMfaReplacementChallenge> BeginReplacementAsync(UserId userId, string accountName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proves possession of the staged candidate: the code is checked against the candidate's secret, never the active one, and its step
    /// is recorded so it cannot be replayed once the candidate is active. Changes nothing about the active authenticator. Returns the
    /// version <see cref="CompleteReplacementAsync"/> must present.
    /// </summary>
    ValueTask<NativeMfaReplacementVerification> VerifyReplacementAsync(
        UserId userId,
        string candidateId,
        int version,
        string totpCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The switch: in one audited unit under the global administrator-set gate, makes the verified candidate the active authenticator and
    /// replaces the recovery codes with a new set, returned once. Judged on its net effect on the Recovery Access posture alone; a decision
    /// made on anything else rolls it back as a retryable conflict. The active authenticator stays in place on every refusal or failure.
    /// </summary>
    ValueTask<NativeMfaReplacementResult> CompleteReplacementAsync(
        UserId userId,
        string candidateId,
        int version,
        AuditActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the account's staged replacement, if any: the operator abandoned it. Never touches the active authenticator.</summary>
    ValueTask<bool> AbandonReplacementAsync(UserId userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The authenticator code presented does not match the secret it was checked against (the pending enrolment's, or the staged
/// replacement's). Nothing was changed and the code may be entered again. It is a distinct type so that a caller never has to read the
/// message to tell a mistyped code from any other <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class NativeMfaCodeNotValidException : InvalidOperationException
{
    /// <summary>The message the service has always given for a code that does not match.</summary>
    public const string DefaultMessage = "The authenticator code is not valid.";

    public NativeMfaCodeNotValidException()
        : base(DefaultMessage)
    {
    }
}

/// <summary>
/// A staged replacement of an enrolled authenticator, shown to the operator once: the candidate's identity and version (kept by the
/// operator's process for the next steps), the secret to add to an authenticator app, and when the candidate expires by the database's
/// clock. The candidate is not an authenticator until it is switched.
/// </summary>
public sealed record NativeMfaReplacementChallenge(
    UserId UserId,
    string CandidateId,
    int Version,
    string OtpAuthUri,
    string ManualEntryKey,
    DateTimeOffset ExpiresAtUtc);

/// <summary>The candidate whose possession was proven, and the version the switch must present.</summary>
public sealed record NativeMfaReplacementVerification(string CandidateId, int Version);

/// <summary>
/// The outcome of a switch. <see cref="RecoveryCodes"/> are the new codes, shown once. <see cref="AlreadyApplied"/> means this candidate's
/// switch had already taken effect in an earlier request whose result was not received (verified against the active authenticator's
/// identity, never assumed): its codes are the ones that request generated and cannot be shown again; regenerate them if they were lost.
/// </summary>
public sealed record NativeMfaReplacementResult(bool AlreadyApplied, IReadOnlyList<string> RecoveryCodes)
{
    /// <summary>The candidate's switch had already been applied by an earlier request; no codes can be shown.</summary>
    public static NativeMfaReplacementResult AppliedEarlier { get; } = new(AlreadyApplied: true, RecoveryCodes: []);
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
