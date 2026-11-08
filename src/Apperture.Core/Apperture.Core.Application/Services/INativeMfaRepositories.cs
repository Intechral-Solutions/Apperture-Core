using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>Persistence for native TOTP credentials. The protected secret is never returned to callers of higher layers.</summary>
public interface INativeTotpCredentialRepository
{
    ValueTask<NativeTotpStoredCredential?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a pending (not yet enrolled) credential: replaces the pending one or creates the first. It never changes an enrolled
    /// credential, however the call interleaves with an enrolment that completes: the condition is evaluated by the database as it
    /// writes. True when the pending credential was written; false when an enrolled credential exists and nothing was changed.
    /// </summary>
    ValueTask<bool> UpsertPendingAsync(
        UserId userId,
        byte[] protectedSecret,
        string protectorKeyId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the pending credential whose secret the caller verified: the row must still be pending <b>and</b> still be the one
    /// identified by <paramref name="expectedCredentialId"/> (the <see cref="NativeTotpStoredCredential.CredentialId"/> read together
    /// with the secret that was checked). The database evaluates both as it writes, so a begin that replaced the pending secret
    /// after the code was checked can never leave the account enrolled on a secret nobody verified. True when the credential was
    /// enrolled; false when nothing was changed (the row is gone, already enrolled, or holds a different secret).
    /// </summary>
    ValueTask<bool> MarkEnrolledAsync(
        UserId userId,
        string expectedCredentialId,
        long lastUsedStep,
        DateTimeOffset enrolledAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask UpdateLastUsedAsync(UserId userId, long lastUsedStep, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Removes the credential. A replacement candidate staged for the user is removed with it: it was staged against a credential that no longer exists.</summary>
    ValueTask DeleteAsync(UserId userId, CancellationToken cancellationToken = default);
}

/// <param name="CredentialId">
/// The identity of the stored secret. A pending write, an enrolment's switch to a replacement secret and a first creation each give
/// the row a new one; enrolling, signing in and the recovery codes do not change it. A caller that checked a code against this
/// secret presents the id back to prove it is still the secret that was checked.
/// </param>
public sealed record NativeTotpStoredCredential(
    UserId UserId,
    byte[] ProtectedSecret,
    string ProtectorKeyId,
    bool IsEnrolled,
    long? LastUsedStep,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EnrolledAtUtc,
    DateTimeOffset? LastVerifiedAtUtc,
    string CredentialId);

/// <summary>Persistence for hashed MFA recovery codes.</summary>
public interface INativeMfaRecoveryCodeRepository
{
    ValueTask<IReadOnlyList<NativeMfaRecoveryCodeRecord>> ListAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the user's codes. A replacement candidate staged for the user is removed first: the codes it would have replaced are no longer the ones the operator saw.</summary>
    ValueTask ReplaceAllAsync(UserId userId, IReadOnlyList<NativeMfaRecoveryCodeRecord> codes, CancellationToken cancellationToken = default);

    ValueTask MarkConsumedAsync(string codeId, DateTimeOffset consumedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Removes the user's codes and, as <see cref="ReplaceAllAsync"/> does, any replacement candidate staged for the user.</summary>
    ValueTask DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default);
}
