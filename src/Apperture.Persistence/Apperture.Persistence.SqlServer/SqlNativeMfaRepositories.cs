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

    public async ValueTask UpsertPendingAsync(
        UserId userId,
        byte[] protectedSecret,
        string protectorKeyId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserTotpCredentials.FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);
        if (entity is null)
        {
            _db.UserTotpCredentials.Add(new UserTotpCredentialEntity
            {
                UserId = userId.Value,
                ProtectedSecret = protectedSecret,
                ProtectorKeyId = protectorKeyId,
                IsEnrolled = false,
                CreatedAtUtc = startedAtUtc
            });
        }
        else
        {
            entity.ProtectedSecret = protectedSecret;
            entity.ProtectorKeyId = protectorKeyId;
            entity.IsEnrolled = false;
            entity.LastUsedStep = null;
            entity.EnrolledAtUtc = null;
            entity.LastVerifiedAtUtc = null;
            entity.CreatedAtUtc = startedAtUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask MarkEnrolledAsync(UserId userId, long lastUsedStep, DateTimeOffset enrolledAtUtc, CancellationToken cancellationToken = default)
    {
        var entity = await _db.UserTotpCredentials.FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken)
            ?? throw new InvalidOperationException("No pending authenticator enrollment exists.");
        entity.IsEnrolled = true;
        entity.LastUsedStep = lastUsedStep;
        entity.EnrolledAtUtc = enrolledAtUtc;
        entity.LastVerifiedAtUtc = enrolledAtUtc;
        await _db.SaveChangesAsync(cancellationToken);
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
            return;
        _db.UserTotpCredentials.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static NativeTotpStoredCredential ToDomain(UserTotpCredentialEntity e) => new(
        new UserId(e.UserId),
        e.ProtectedSecret,
        e.ProtectorKeyId,
        e.IsEnrolled,
        e.LastUsedStep,
        e.CreatedAtUtc,
        e.EnrolledAtUtc,
        e.LastVerifiedAtUtc);
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
        var existing = await _db.UserMfaRecoveryCodes.Where(e => e.UserId == userId.Value).ToListAsync(cancellationToken);
        _db.UserMfaRecoveryCodes.RemoveRange(existing);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
