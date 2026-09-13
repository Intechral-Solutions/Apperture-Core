using Apperture.Core.Application.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// Stores normalized avatar renditions as database rows. Keeping bytes in SQL avoids persisting any
/// environment-specific path on the user record, so a future cloud-backed store can replace this
/// implementation without a data shape change on <c>Users</c>.
/// </summary>
public sealed class SqlUserProfileImageStore : IUserProfileImageStore
{
    private readonly AppertureDbContext _db;

    public SqlUserProfileImageStore(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask SaveAsync(
        string userId,
        string imageKey,
        IReadOnlyList<ProfileImageRendition> renditions,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var rendition in renditions)
        {
            _db.UserProfileImages.Add(new UserProfileImageEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                ImageKey = imageKey,
                Size = rendition.Size,
                ContentType = rendition.ContentType,
                Content = rendition.Content,
                ByteLength = rendition.Content.Length,
                CreatedUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ProfileImageContent?> GetAsync(
        string userId,
        string imageKey,
        int size,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.UserProfileImages.AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.UserId == userId && i.ImageKey == imageKey && i.Size == size,
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : new ProfileImageContent(row.ContentType, row.Content);
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteForUserAsync(
        string userId,
        string? keepImageKey,
        CancellationToken cancellationToken = default)
    {
        var query = _db.UserProfileImages.Where(i => i.UserId == userId);
        if (!string.IsNullOrWhiteSpace(keepImageKey))
            query = query.Where(i => i.ImageKey != keepImageKey);

        var doomed = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        if (doomed.Count == 0)
            return 0;

        var retiredKeys = doomed.Select(i => i.ImageKey).Distinct(StringComparer.Ordinal).Count();
        _db.UserProfileImages.RemoveRange(doomed);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return retiredKeys;
    }
}
