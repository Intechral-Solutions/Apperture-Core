using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// SQL persistence for platform users and identity links.
/// </summary>
public sealed class SqlUserRepository : IUserRepository
{
    /// <summary>
    /// Avoid rewriting <see cref="IdentityLinkEntity.LastAuthenticatedAt"/> on every auth check during concurrent page loads.
    /// </summary>
    internal static readonly TimeSpan LastAuthenticatedAtWriteInterval = TimeSpan.FromMinutes(1);

    private readonly AppertureDbContext _db;
    private readonly ILogger<SqlUserRepository> _logger;

    public SqlUserRepository(AppertureDbContext db, ILogger<SqlUserRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<int> GetUserCountAsync(CancellationToken cancellationToken = default) =>
        await _db.Users.CountAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default)
    {
        var userEntity = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id.Value, cancellationToken);
        if (userEntity is null)
            return null;

        var links = await _db.IdentityLinks.AsNoTracking()
            .Where(l => l.UserId == id.Value)
            .ToListAsync(cancellationToken);
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == id.Value)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        return ToDomain(userEntity, links, roleIds);
    }

    /// <inheritdoc />
    public async ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default)
    {
        var link = await _db.IdentityLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Source == (int)source && l.ExternalId == externalId, cancellationToken);
        if (link is null)
            return null;

        return await GetByIdAsync(new UserId(link.UserId), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
    {
        var userEntity = new UserEntity
        {
            Id = user.Id.Value,
            DisplayName = user.DisplayName,
            IsBreakGlassEligible = user.IsBreakGlassEligible,
            DisplayTimeZoneId = user.DisplayTimeZoneId
        };
        _db.Users.Add(userEntity);

        foreach (var link in user.IdentityLinks)
        {
            _db.IdentityLinks.Add(new IdentityLinkEntity
            {
                Id = Guid.NewGuid().ToString("N")[..24],
                UserId = user.Id.Value,
                Source = (int)link.Source,
                ExternalId = link.ExternalId,
                LastAuthenticatedAt = link.LastAuthenticatedAt
            });
        }

        foreach (var roleId in user.AssignedRoleIds)
        {
            _db.UserRoles.Add(new UserRoleEntity
            {
                UserId = user.Id.Value,
                RoleId = roleId.Value
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Users.FirstOrDefaultAsync(e => e.Id == user.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"User {user.Id.Value} not found.");

        entity.DisplayName = user.DisplayName;
        entity.IsBreakGlassEligible = user.IsBreakGlassEligible;
        entity.DisplayTimeZoneId = user.DisplayTimeZoneId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        try
        {
            var writeOnlyIfOlderThan = at - LastAuthenticatedAtWriteInterval;

            await _db.IdentityLinks
                .Where(l => l.Source == (int)source && l.ExternalId == externalId)
                .Where(l => l.LastAuthenticatedAt == null || l.LastAuthenticatedAt < writeOnlyIfOlderThan)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(l => l.LastAuthenticatedAt, at),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogDebug(
                ex,
                "Ignored optimistic concurrency while updating LastAuthenticatedAt for {IdentitySource}/{ExternalId}.",
                source,
                externalId);
        }
    }

    /// <inheritdoc />
    public async ValueTask<(IReadOnlyList<User> Page, int TotalCount)> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var totalCount = await _db.Users.CountAsync(cancellationToken);
        var userEntities = await _db.Users.AsNoTracking()
            .OrderBy(u => u.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (userEntities.Count == 0)
            return (Array.Empty<User>(), totalCount);

        var userIds = userEntities.Select(u => u.Id).ToList();
        var links = await _db.IdentityLinks.AsNoTracking()
            .Where(l => userIds.Contains(l.UserId))
            .ToListAsync(cancellationToken);
        var userRoles = await _db.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .ToListAsync(cancellationToken);

        var linkByUser = links.GroupBy(l => l.UserId).ToDictionary(g => g.Key, g => g.ToList());
        var roleIdsByUser = userRoles.GroupBy(ur => ur.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).ToList());

        var page = userEntities.ConvertAll(e =>
        {
            var userLinks = linkByUser.GetValueOrDefault(e.Id) ?? new List<IdentityLinkEntity>();
            var roleIds = roleIdsByUser.GetValueOrDefault(e.Id) ?? new List<string>();
            return ToDomain(e, userLinks, roleIds);
        });
        return (page, totalCount);
    }

    /// <inheritdoc />
    public async ValueTask AddIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
    {
        var exists = await _db.IdentityLinks.AnyAsync(
            l => l.Source == (int)source && l.ExternalId == externalId, cancellationToken);
        if (exists)
            throw new InvalidOperationException("An identity link with this source and external id already exists.");

        var userExists = await _db.Users.AnyAsync(u => u.Id == userId.Value, cancellationToken);
        if (!userExists)
            throw new InvalidOperationException($"User {userId.Value} not found.");

        _db.IdentityLinks.Add(new IdentityLinkEntity
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            UserId = userId.Value,
            Source = (int)source,
            ExternalId = externalId,
            LastAuthenticatedAt = null
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask RemoveIdentityLinkAsync(UserId userId, IdentitySource source, string externalId, CancellationToken cancellationToken = default)
    {
        var link = await _db.IdentityLinks
            .FirstOrDefaultAsync(l => l.UserId == userId.Value && l.Source == (int)source && l.ExternalId == externalId, cancellationToken);
        if (link is null)
            return;

        var linkCount = await _db.IdentityLinks.CountAsync(l => l.UserId == userId.Value, cancellationToken);
        if (linkCount <= 1)
            throw new InvalidOperationException("Cannot remove the last identity link from a user.");

        _db.IdentityLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static User ToDomain(UserEntity userEntity, List<IdentityLinkEntity> links, List<string> roleIds)
    {
        var identityLinks = links
            .Select(l => new IdentityLink(
                (IdentitySource)l.Source,
                l.ExternalId,
                l.LastAuthenticatedAt))
            .ToList();
        var assignedRoleIds = roleIds.Select(r => new RoleId(r)).ToList();

        return new User(
            new UserId(userEntity.Id),
            userEntity.DisplayName,
            identityLinks,
            assignedRoleIds,
            userEntity.IsBreakGlassEligible,
            userEntity.DisplayTimeZoneId);
    }
}
