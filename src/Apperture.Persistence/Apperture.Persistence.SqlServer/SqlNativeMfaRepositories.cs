using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlNativeTotpCredentialRepository : INativeTotpCredentialRepository
{
    private readonly AppertureDbContext _db;

    public SqlNativeTotpCredentialRepository(AppertureDbContext db) => _db = db;

    public async ValueTask<NativeTotpStoredCredential?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserTotpCredentials.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async ValueTask<bool> UpsertPendingAsync(
        UserId userId,
        byte[] protectedSecret,
        string protectorKeyId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        // A pending write replaces a pending row or creates the first one; it never touches an enrolled row. Each step is one
        // statement whose condition the database evaluates as it writes, so a check made earlier by the caller is not what protects
        // an enrolment that completed in between. The loop only repeats when a concurrent first creation took the key.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var replaced = await _db.UserTotpCredentials
                .Where(e => e.UserId == userId.Value && !e.IsEnrolled)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(e => e.ProtectedSecret, protectedSecret)
                        .SetProperty(e => e.ProtectorKeyId, protectorKeyId)
                        .SetProperty(e => e.LastUsedStep, (long?)null)
                        .SetProperty(e => e.EnrolledAtUtc, (DateTimeOffset?)null)
                        .SetProperty(e => e.LastVerifiedAtUtc, (DateTimeOffset?)null)
                        .SetProperty(e => e.CreatedAtUtc, startedAtUtc)
                        // A new secret is a new identity: a verification of the previous pending secret cannot enroll this one.
                        .SetProperty(e => e.CredentialId, NewCredentialId()),
                    cancellationToken);
            if (replaced == 1)
                return true;

            // No pending row. A row that exists now is enrolled: it stays, and nothing is written.
            if (await _db.UserTotpCredentials.AsNoTracking().AnyAsync(e => e.UserId == userId.Value, cancellationToken))
                return false;

            var created = new UserTotpCredentialEntity
            {
                UserId = userId.Value,
                ProtectedSecret = protectedSecret,
                ProtectorKeyId = protectorKeyId,
                IsEnrolled = false,
                CreatedAtUtc = startedAtUtc,
                CredentialId = NewCredentialId()
            };
            _db.UserTotpCredentials.Add(created);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (SqlUniqueKeyViolation.IsOnIndex(ex, "PK_UserTotpCredentials"))
            {
                // Another begin created the row first: decide again against what is committed now (pending: replace it;
                // enrolled: leave it).
                _db.Entry(created).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("The pending authenticator enrollment could not be written.");
    }

    /// <inheritdoc />
    public async ValueTask<bool> MarkEnrolledAsync(
        UserId userId,
        string expectedCredentialId,
        long lastUsedStep,
        DateTimeOffset enrolledAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCredentialId);

        // One statement, whose condition the database evaluates as it writes: the row must still be pending and still hold the secret
        // that was verified. The identity does not change (the secret is the same one), so the enrolled row keeps naming it.
        var enrolled = await _db.UserTotpCredentials
            .Where(e => e.UserId == userId.Value && !e.IsEnrolled && e.CredentialId == expectedCredentialId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.IsEnrolled, true)
                    .SetProperty(e => e.LastUsedStep, (long?)lastUsedStep)
                    .SetProperty(e => e.EnrolledAtUtc, (DateTimeOffset?)enrolledAtUtc)
                    .SetProperty(e => e.LastVerifiedAtUtc, (DateTimeOffset?)enrolledAtUtc),
                cancellationToken);
        return enrolled == 1;
    }

    public async ValueTask UpdateLastUsedAsync(UserId userId, long lastUsedStep, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserTotpCredentials.FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);
        if (entity is null)
            return;
        entity.LastUsedStep = lastUsedStep;
        entity.LastVerifiedAtUtc = verifiedAtUtc;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserTotpCredentials.FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);
        if (entity is null)
        {
            await RemoveCandidatesAsync(_db, userId, cancellationToken);
            return;
        }

        _db.UserTotpCredentials.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        // After the credential row (lock order: credential, candidates, codes). A candidate staged against the removed credential
        // can never be switched; its protected secret is removed rather than left to expire.
        await RemoveCandidatesAsync(_db, userId, cancellationToken);
    }

    /// <summary>The new identity of a stored secret.</summary>
    internal static string NewCredentialId() => Guid.NewGuid().ToString("N");

    internal static Task<int> RemoveCandidatesAsync(AppertureDbContext db, UserId userId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [UserTotpReplacementCandidates] WHERE [UserId] = {userId.Value};",
            cancellationToken);

    private static NativeTotpStoredCredential ToDomain(UserTotpCredentialEntity e) => new(
        new UserId(e.UserId),
        e.ProtectedSecret,
        e.ProtectorKeyId,
        e.IsEnrolled,
        e.LastUsedStep,
        e.CreatedAtUtc,
        e.EnrolledAtUtc,
        e.LastVerifiedAtUtc,
        e.CredentialId);
}

public sealed class SqlNativeMfaRecoveryCodeRepository : INativeMfaRecoveryCodeRepository
{
    private readonly AppertureDbContext _db;

    public SqlNativeMfaRecoveryCodeRepository(AppertureDbContext db) => _db = db;

    public async ValueTask<IReadOnlyList<NativeMfaRecoveryCodeRecord>> ListAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.UserMfaRecoveryCodes.AsNoTracking()
            .Where(e => e.UserId == userId.Value)
            .ToListAsync(cancellationToken);
        return rows.Select(e => new NativeMfaRecoveryCodeRecord(
            e.Id,
            new UserId(e.UserId),
            e.CodeHash,
            e.Salt,
            e.Algorithm,
            e.ConsumedAtUtc)).ToList();
    }

    public async ValueTask ReplaceAllAsync(UserId userId, IReadOnlyList<NativeMfaRecoveryCodeRecord> codes, CancellationToken cancellationToken = default)
    {
        // Before the codes (lock order: credential, candidates, codes): a candidate staged earlier would replace codes the operator
        // has not seen.
        await SqlNativeTotpCredentialRepository.RemoveCandidatesAsync(_db, userId, cancellationToken);

        var existing = await _db.UserMfaRecoveryCodes.Where(e => e.UserId == userId.Value).ToListAsync(cancellationToken);
        _db.UserMfaRecoveryCodes.RemoveRange(existing);
        foreach (var code in codes)
        {
            _db.UserMfaRecoveryCodes.Add(new UserMfaRecoveryCodeEntity
            {
                Id = code.Id,
                UserId = userId.Value,
                CodeHash = code.CodeHash,
                Salt = code.Salt,
                Algorithm = code.Algorithm,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ConsumedAtUtc = code.ConsumedAtUtc
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkConsumedAsync(string codeId, DateTimeOffset consumedAtUtc, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserMfaRecoveryCodes.FirstOrDefaultAsync(e => e.Id == codeId, cancellationToken);
        if (entity is null)
            return;
        entity.ConsumedAtUtc = consumedAtUtc;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        await SqlNativeTotpCredentialRepository.RemoveCandidatesAsync(_db, userId, cancellationToken);

        var existing = await _db.UserMfaRecoveryCodes.Where(e => e.UserId == userId.Value).ToListAsync(cancellationToken);
        _db.UserMfaRecoveryCodes.RemoveRange(existing);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
