using Apperture.Core.Application.Accounts;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlAccountActionTokenRepository : IAccountActionTokenRepository
{
    private readonly AppertureDbContext _db;

    public SqlAccountActionTokenRepository(AppertureDbContext db) => _db = db;

    public async ValueTask AddAsync(AccountActionTokenRecord token, CancellationToken cancellationToken = default)
    {
        _db.AccountActionTokens.Add(ToEntity(token));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<AccountActionTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var entity = await _db.AccountActionTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async ValueTask<IReadOnlyList<AccountActionTokenRecord>> ListActiveAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.AccountActionTokens
            .Where(t =>
                t.UserId == userId.Value
                && t.Purpose == (int)purpose
                && t.ConsumedAtUtc == null
                && t.RevokedAtUtc == null
                && t.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async ValueTask<AccountActionTokenRecord?> FindLatestAsync(
        UserId userId,
        AccountActionTokenPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.AccountActionTokens
            .Where(t => t.UserId == userId.Value && t.Purpose == (int)purpose)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async ValueTask UpdateAsync(AccountActionTokenRecord token, CancellationToken cancellationToken = default)
    {
        var entity = await _db.AccountActionTokens
            .FirstOrDefaultAsync(t => t.Id == token.Id, cancellationToken)
            ?? throw new InvalidOperationException("Account action token was not found.");
        entity.ConsumedAtUtc = token.ConsumedAtUtc;
        entity.RevokedAtUtc = token.RevokedAtUtc;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static AccountActionTokenEntity ToEntity(AccountActionTokenRecord token) => new()
    {
        Id = token.Id,
        UserId = token.UserId.Value,
        Purpose = (int)token.Purpose,
        TokenHash = token.TokenHash,
        DestinationEmail = token.DestinationEmail,
        CreatedAtUtc = token.CreatedAtUtc,
        ExpiresAtUtc = token.ExpiresAtUtc,
        ConsumedAtUtc = token.ConsumedAtUtc,
        RevokedAtUtc = token.RevokedAtUtc
    };

    private static AccountActionTokenRecord ToRecord(AccountActionTokenEntity entity) => new(
        entity.Id,
        new UserId(entity.UserId),
        (AccountActionTokenPurpose)entity.Purpose,
        entity.TokenHash,
        entity.DestinationEmail,
        entity.CreatedAtUtc,
        entity.ExpiresAtUtc,
        entity.ConsumedAtUtc,
        entity.RevokedAtUtc);
}
