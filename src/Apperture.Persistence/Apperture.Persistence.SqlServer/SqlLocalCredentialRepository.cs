using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for local credentials. One row per user.
/// </summary>
public sealed class SqlLocalCredentialRepository : ILocalCredentialRepository
{
    private readonly AppertureDbContext _db;

    public SqlLocalCredentialRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<LocalCredentialData?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.LocalCredentials.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);
        return entity is null ? null : new LocalCredentialData(
            new UserId(entity.UserId),
            entity.PasswordHash,
            entity.Salt,
            entity.Algorithm,
            entity.IsDisabled);
    }

    /// <inheritdoc />
    public async ValueTask SetPasswordAsync(UserId userId, string passwordHash, string salt, string algorithm, CancellationToken cancellationToken = default)
    {
        var entity = await _db.LocalCredentials.FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);
        if (entity is null)
        {
            _db.LocalCredentials.Add(new LocalCredentialEntity
            {
                UserId = userId.Value,
                PasswordHash = passwordHash,
                Salt = salt,
                Algorithm = algorithm,
                IsDisabled = false
            });
        }
        else
        {
            entity.PasswordHash = passwordHash;
            entity.Salt = salt;
            entity.Algorithm = algorithm;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SetDisabledAsync(UserId userId, bool disabled, CancellationToken cancellationToken = default)
    {
        var entity = await _db.LocalCredentials.FirstOrDefaultAsync(e => e.UserId == userId.Value, cancellationToken);
        if (entity is not null)
        {
            entity.IsDisabled = disabled;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
