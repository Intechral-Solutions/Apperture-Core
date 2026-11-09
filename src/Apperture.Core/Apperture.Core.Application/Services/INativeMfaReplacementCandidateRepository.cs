using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Persistence for the replacement of an enrolled authenticator without removing it first: a candidate secret is staged beside the
/// active one, the operator proves possession of it, and a short switch makes it the active secret. The active authenticator and
/// its recovery codes change only when <see cref="SwitchAsync"/> reports <see cref="NativeMfaCandidateOutcome.Applied"/>.
/// </summary>
/// <remarks>
/// <para><b>Time.</b> Every expiry decision is made by the database with its own clock inside the statement that acts on the
/// candidate; no caller-supplied time takes part, and no method takes one. A candidate is expired once the database's clock has
/// reached its expiry.</para>
/// <para><b>Failure never changes the active authenticator.</b> Every outcome other than <c>Applied</c> leaves the active secret,
/// its last used step, its identity and the recovery codes exactly as they were. Staging and verifying change nothing of the
/// active credential at all.</para>
/// <para><b>Transactions.</b> Staging, verifying and discarding join the caller's transaction when there is one; without one, each is
/// one transaction of its own. The switch <b>requires</b> the caller's open transaction (the audited unit's, under the global
/// administrator gate) and never commits on its own: without one it throws <see cref="InvalidOperationException"/> before any
/// statement. Staging and verifying hold no gate and no lock across operator input.</para>
/// <para><b>Lock order</b> (every method that takes more than one lock follows it): the user's credential row, then the user's
/// candidate rows, then the user's recovery code rows.</para>
/// </remarks>
public interface INativeMfaReplacementCandidateRepository
{
    /// <summary>
    /// Stages a new candidate for the user's enrolled authenticator, superseding any live candidate in the same transaction. The
    /// candidate expires <paramref name="lifetime"/> after the database's clock at this call. Staging requires an enrolled
    /// authenticator (a replacement replaces something); without one the outcome is
    /// <see cref="NativeMfaCandidateOutcome.NoActiveEnrollment"/> and nothing is written.
    /// </summary>
    ValueTask<NativeMfaCandidateStageResult> StageAsync(
        UserId userId,
        byte[] protectedSecret,
        string protectorKeyId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a candidate of this user by id. A candidate of another user, and one that was purged, read as null. The result carries
    /// the database's own judgement of whether it has expired. A superseded or consumed candidate carries no secret.
    /// </summary>
    ValueTask<NativeMfaReplacementCandidate?> GetAsync(UserId userId, string candidateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the operator proved possession of the candidate's secret, remembering the matched time step so the same code
    /// cannot be replayed once the candidate is active. Compare-and-set on the candidate's version: only a candidate that is
    /// still staged, unexpired, of this user and staged against the credential that is still active, at <paramref name="expectedVersion"/>,
    /// is verified. The result carries the version the switch must present.
    /// </summary>
    ValueTask<NativeMfaCandidateVerifyResult> MarkVerifiedAsync(
        UserId userId,
        string candidateId,
        int expectedVersion,
        long matchedStep,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The switch: in one atomic step, makes the verified candidate's secret the user's active secret, replaces the active recovery
    /// codes with <see cref="NativeMfaSwitchRequest.RecoveryCodes"/> and consumes the candidate. It is applied only when the
    /// candidate is verified, unexpired by the database's clock, at the presented version, of this user, and was staged against the
    /// credential that is active now; then the active credential's <see cref="NativeTotpStoredCredential.CredentialId"/> becomes the
    /// candidate's id, so that whoever switched it can tell afterwards whether this candidate's switch is the active authenticator (a
    /// <see cref="NativeMfaCandidateOutcome.Consumed"/> result alone does not say so). The user must be named by the id it is stored under,
    /// and the codes must be new, unused, distinct and of the stored form; anything else is refused before any statement.
    /// </summary>
    /// <exception cref="InvalidOperationException">The caller has no open transaction on the repository's context. The switch runs
    /// only inside the audited unit, under the administrator-set gate, and commits or rolls back with it; nothing is written.</exception>
    ValueTask<NativeMfaSwitchResult> SwitchAsync(NativeMfaSwitchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes every candidate of the user (an abandoned replacement). It never touches the active credential. Returns how many rows were removed.</summary>
    ValueTask<int> DiscardAsync(UserId userId, CancellationToken cancellationToken = default);
}

/// <summary>The lifecycle of a candidate. A candidate moves forward only: staged, verified, then consumed by the switch; a newer candidate supersedes a live one.</summary>
public enum NativeMfaCandidateStatus
{
    Staged = 0,
    Verified = 1,
    Superseded = 2,
    Consumed = 3,
}

/// <summary>Why a candidate operation did or did not apply. Anything other than <see cref="Applied"/> changed nothing.</summary>
public enum NativeMfaCandidateOutcome
{
    /// <summary>The operation took effect.</summary>
    Applied,

    /// <summary>The user has no enrolled authenticator, so there is nothing to replace.</summary>
    NoActiveEnrollment,

    /// <summary>No candidate with this id exists for this user (never staged, another user's, or purged).</summary>
    NotFound,

    /// <summary>A newer candidate was staged: this one can never become active.</summary>
    Superseded,

    /// <summary>The switch already consumed this candidate.</summary>
    Consumed,

    /// <summary>The database's clock has reached the candidate's expiry.</summary>
    Expired,

    /// <summary>The caller presented a version other than the candidate's current one.</summary>
    VersionMismatch,

    /// <summary>The candidate was already verified (verification applies to a staged candidate once).</summary>
    AlreadyVerified,

    /// <summary>The candidate has not been verified, so it cannot be switched.</summary>
    NotVerified,

    /// <summary>The active credential is no longer the one the candidate was staged against (switched, reset or re-enrolled since).</summary>
    ActiveEnrollmentChanged,
}

/// <param name="IsExpired">Whether the <i>database's</i> clock had reached <paramref name="ExpiresAtUtc"/> when the row was read.</param>
/// <param name="ProtectedSecret">The candidate's protected secret while it is live (staged or verified); empty once superseded or consumed.</param>
public sealed record NativeMfaReplacementCandidate(
    string CandidateId,
    UserId UserId,
    byte[] ProtectedSecret,
    string ProtectorKeyId,
    string StagedAgainstCredentialId,
    NativeMfaCandidateStatus Status,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? VerifiedAtUtc,
    long? MatchedStep,
    bool IsExpired);

/// <summary>The result of staging: the outcome, and the candidate when it was staged.</summary>
public sealed record NativeMfaCandidateStageResult(NativeMfaCandidateOutcome Outcome, NativeMfaReplacementCandidate? Candidate)
{
    public bool IsApplied => Outcome == NativeMfaCandidateOutcome.Applied;
}

/// <summary>The result of verifying: the outcome, and the version the switch must present when it applied.</summary>
public sealed record NativeMfaCandidateVerifyResult(NativeMfaCandidateOutcome Outcome, int? Version)
{
    public bool IsApplied => Outcome == NativeMfaCandidateOutcome.Applied;
}

/// <param name="ExpectedVersion">The version <see cref="NativeMfaCandidateVerifyResult.Version"/> returned.</param>
/// <param name="RecoveryCodes">The new recovery codes (hashed before the call) that replace the active ones with the secret.</param>
/// <param name="SwitchedAtUtc">Recorded as the new enrollment's time. It is not used to decide expiry.</param>
public sealed record NativeMfaSwitchRequest(
    UserId UserId,
    string CandidateId,
    int ExpectedVersion,
    IReadOnlyList<NativeMfaRecoveryCodeRecord> RecoveryCodes,
    DateTimeOffset SwitchedAtUtc);

/// <summary>The result of the switch: the outcome, and the identity of the new active credential when it applied.</summary>
public sealed record NativeMfaSwitchResult(NativeMfaCandidateOutcome Outcome, string? NewCredentialId)
{
    public bool IsApplied => Outcome == NativeMfaCandidateOutcome.Applied;
}
