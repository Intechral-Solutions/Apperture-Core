using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>Persistence for native TOTP credentials. The protected secret is never returned to callers of higher layers.</summary>
public interface INativeTotpCredentialRepository
{
    ValueTask<NativeTotpStoredCredential?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);

    ValueTask UpsertPendingAsync(
        UserId userId,
        byte[] protectedSecret,
        string protectorKeyId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask MarkEnrolledAsync(UserId userId, long lastUsedStep, DateTimeOffset enrolledAtUtc, CancellationToken cancellationToken = default);

    ValueTask UpdateLastUsedAsync(UserId userId, long lastUsedStep, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(UserId userId, CancellationToken cancellationToken = default);
}

public sealed record NativeTotpStoredCredential(
    UserId UserId,
    byte[] ProtectedSecret,
    string ProtectorKeyId,
    bool IsEnrolled,
    long? LastUsedStep,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EnrolledAtUtc,
    DateTimeOffset? LastVerifiedAtUtc);

/// <summary>Persistence for hashed MFA recovery codes.</summary>
public interface INativeMfaRecoveryCodeRepository
{
    ValueTask<IReadOnlyList<NativeMfaRecoveryCodeRecord>> ListAsync(UserId userId, CancellationToken cancellationToken = default);

    ValueTask ReplaceAllAsync(UserId userId, IReadOnlyList<NativeMfaRecoveryCodeRecord> codes, CancellationToken cancellationToken = default);

    ValueTask MarkConsumedAsync(string codeId, DateTimeOffset consumedAtUtc, CancellationToken cancellationToken = default);

    ValueTask DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default);
}
