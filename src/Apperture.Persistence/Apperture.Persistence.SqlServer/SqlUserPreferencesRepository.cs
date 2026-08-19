using Apperture.Core.Application.Accounts;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>SQL persistence for typed per-user preferences (one row per user).</summary>
public sealed class SqlUserPreferencesRepository : IUserPreferencesRepository
{
    private readonly AppertureDbContext _db;

    public SqlUserPreferencesRepository(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<UserPreferences?> GetAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var row = await _db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : UserPreferences.FromStorage(row.Appearance, row.TimeFormat);
    }

    /// <inheritdoc />
    public async ValueTask UpsertAsync(
        UserId userId,
        UserPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await _db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            _db.UserPreferences.Add(new UserPreferenceEntity
            {
                UserId = userId.Value,
                Appearance = preferences.Appearance.ToString(),
                TimeFormat = preferences.TimeFormat.ToString(),
                CreatedUtc = now,
                UpdatedUtc = now
            });
        }
        else
        {
            row.Appearance = preferences.Appearance.ToString();
            row.TimeFormat = preferences.TimeFormat.ToString();
            row.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
