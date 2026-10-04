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
    public async ValueTask<User?> FindDesignatedEmergencyUserAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(e => e.IsBreakGlassEligible, cancellationToken);
        if (entity is null)
            return null;
        return await GetByIdAsync(new UserId(entity.Id), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<User?> GetByIdentityLinkAsync(IdentitySource source, string externalId, CancellationToken cancellationToken = default)
    {
        if (source == IdentitySource.Entra)
        {
            throw new ArgumentException(
                "Entra identity lookup requires tenant id and object id. Use ProviderIdentityKey.Entra(tenantId, objectId).",
                nameof(source));
        }

        var link = await _db.IdentityLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Source == (int)source && l.ExternalId == externalId, cancellationToken);
        if (link is null)
            return null;

        return await GetByIdAsync(new UserId(link.UserId), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<User?> GetByIdentityLinkAsync(ProviderIdentityKey identity, CancellationToken cancellationToken = default)
    {
        var link = await FindLinkEntityAsync(identity, tracking: false, cancellationToken);
        if (link is null)
            return null;

        return await GetByIdAsync(new UserId(link.UserId), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(User user, CancellationToken cancellationToken = default)
    {
        RequireNewUserIdentity(user);

        foreach (var link in user.IdentityLinks)
        {
            if (await IdentityExistsAsync(link, cancellationToken))
                throw new InvalidOperationException("An identity link with this source and external id already exists.");
        }

        var userEntity = new UserEntity
        {
            Id = user.Id.Value,
            Username = user.Username,
            DisplayName = user.DisplayName,
            IsBreakGlassEligible = user.IsBreakGlassEligible,
            DisplayTimeZoneId = user.DisplayTimeZoneId,
            EmailAddress = user.EmailAddress,
            EmailVerifiedAtUtc = user.EmailVerifiedAtUtc,
            PendingEmailAddress = user.PendingEmailAddress
        };
        ApplyAvatar(userEntity, user.Avatar);

        // The user row, its ledger row, links and roles commit in one SaveChanges, so a name is never
        // left reserved by a user that failed to persist.
        var added = new List<object>
        {
            userEntity,
            new UsernameLedgerEntity
            {
                Username = user.Username,
                UserId = user.Id.Value,
                AssignedAtUtc = DateTimeOffset.UtcNow
            }
        };
        added.AddRange(user.IdentityLinks.Select(link => ToEntity(user.Id, link)));
        added.AddRange(user.AssignedRoleIds.Select(roleId => new UserRoleEntity
        {
            UserId = user.Id.Value,
            RoleId = roleId.Value
        }));

        foreach (var entity in added)
            _db.Add(entity);

        try
        {
            await SaveIdentityChangesAsync(cancellationToken);
        }
        catch
        {
            Detach(added);
            throw;
        }
    }

    /// <summary>
    /// Defensive invariant for every creation path: a new user carries a canonical, policy-shaped
    /// username, and its Local link (at most one) mirrors that username.
    /// </summary>
    private static void RequireNewUserIdentity(User user)
    {
        if (string.IsNullOrWhiteSpace(user.Username)
            || !UsernamePolicy.IsCanonical(user.Username)
            || !UsernamePolicy.IsValidFormat(user.Username))
        {
            throw new ArgumentException(
                "A new user needs a canonical username that satisfies the username policy.",
                nameof(user));
        }

        var localLinks = user.IdentityLinks.Where(l => l.Source == IdentitySource.Local).ToList();
        if (localLinks.Count > 1)
            throw new ArgumentException("A user can have at most one Local identity link.", nameof(user));
        if (localLinks.Count == 1 && !string.Equals(localLinks[0].ExternalId, user.Username, StringComparison.Ordinal))
        {
            throw new UsernameException(
                UsernameErrorCodes.Mismatch,
                "A Local identity link must use the user's username.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<UsernameChangeOutcome> ChangeUsernameAsync(
        UserId userId,
        string expectedCurrentUsername,
        string newUsername,
        string retiredReason,
        string? newDisplayName = null,
        CancellationToken cancellationToken = default)
    {
        if (!UsernamePolicy.IsCanonical(newUsername) || !UsernamePolicy.IsValidFormat(newUsername))
            throw new ArgumentException("The new username must be canonical and satisfy the username policy.", nameof(newUsername));

        // Retry-on-failure is enabled, so an explicit transaction must run inside the execution strategy.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async ct =>
            {
                var tracked = new List<object>();
                try
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                    var outcome = await ChangeUsernameCoreAsync(
                        userId, expectedCurrentUsername, newUsername, retiredReason, newDisplayName, tracked, ct);
                    await _db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return outcome;
                }
                catch (Exception ex) when (IsUsernameUniqueViolation(ex))
                {
                    Detach(tracked);
                    throw new UsernameException(
                        UsernameErrorCodes.Taken,
                        "That username belongs, or once belonged, to another user.");
                }
                catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsCurrentUsernameIndexViolation(ex))
                {
                    Detach(tracked);
                    throw new UsernameException(
                        UsernameErrorCodes.ConcurrentChange,
                        "The user was changed by another operation. Reload and try again.");
                }
                catch
                {
                    Detach(tracked);
                    throw;
                }
            },
            cancellationToken);
    }

    private async Task<UsernameChangeOutcome> ChangeUsernameCoreAsync(
        UserId userId,
        string expectedCurrentUsername,
        string newUsername,
        string retiredReason,
        string? newDisplayName,
        List<object> tracked,
        CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(e => e.Id == userId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId.Value} not found.");
        tracked.Add(user);
        if (!string.Equals(user.Username, expectedCurrentUsername, StringComparison.Ordinal))
        {
            throw new UsernameException(
                UsernameErrorCodes.ConcurrentChange,
                "The user's username changed. Reload and try again.");
        }

        var outcome = UsernameChangeOutcome.Unchanged;
        if (!string.Equals(user.Username, newUsername, StringComparison.Ordinal))
        {
            var now = DateTimeOffset.UtcNow;
            var current = user.Username;

            // Retire first: the filtered one-current-name-per-user index allows only one live row.
            var retired = await _db.UsernameLedger
                .Where(l => l.Username == current && l.UserId == userId.Value && l.RetiredAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(l => l.RetiredAtUtc, now)
                        .SetProperty(l => l.RetiredReason, retiredReason),
                    cancellationToken);
            if (retired != 1)
            {
                throw new UsernameException(
                    UsernameErrorCodes.ConcurrentChange,
                    "The user's current username is not recorded as current. Reload and try again.");
            }

            // Reclaim only this user's own retired name. A row owned by anyone else is never touched,
            // and the insert below then collides on the ledger primary key.
            var reclaimed = await _db.UsernameLedger
                .Where(l => l.Username == newUsername && l.UserId == userId.Value && l.RetiredAtUtc != null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(l => l.RetiredAtUtc, (DateTimeOffset?)null)
                        .SetProperty(l => l.RetiredReason, (string?)null),
                    cancellationToken);
            if (reclaimed == 0)
            {
                // Written immediately, before Users and the Local link, so the ledger primary key is what
                // decides that a name is taken (EF would otherwise order the link update first).
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO [UsernameLedger] ([Username], [UserId], [AssignedAtUtc], [RetiredAtUtc], [RetiredReason]) VALUES ({newUsername}, {userId.Value}, {now}, NULL, NULL)",
                    cancellationToken);
                outcome = UsernameChangeOutcome.Assigned;
            }
            else
            {
                outcome = UsernameChangeOutcome.Reclaimed;
            }

            user.Username = newUsername;

            // The Local link's external id mirrors the username (at most one Local link per user).
            var localLink = await _db.IdentityLinks.FirstOrDefaultAsync(
                l => l.UserId == userId.Value && l.Source == (int)IdentitySource.Local,
                cancellationToken);
            if (localLink is not null)
            {
                localLink.ExternalId = newUsername;
                tracked.Add(localLink);
            }
        }

        if (newDisplayName is not null)
            user.DisplayName = newDisplayName;

        return outcome;
    }

    /// <inheritdoc />
    public async ValueTask<UsernameLedgerEntry?> FindUsernameLedgerEntryAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var row = await _db.UsernameLedger.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Username == username, cancellationToken);
        return row is null
            ? null
            : new UsernameLedgerEntry(row.Username, new UserId(row.UserId), row.AssignedAtUtc, row.RetiredAtUtc, row.RetiredReason);
    }

    private void Detach(IEnumerable<object> entities)
    {
        foreach (var entity in entities)
            _db.Entry(entity).State = EntityState.Detached;
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Users.FirstOrDefaultAsync(e => e.Id == user.Id.Value, cancellationToken)
            ?? throw new InvalidOperationException($"User {user.Id.Value} not found.");

        entity.DisplayName = user.DisplayName;
        entity.IsBreakGlassEligible = user.IsBreakGlassEligible;
        entity.DisplayTimeZoneId = user.DisplayTimeZoneId;
        entity.EmailAddress = user.EmailAddress;
        entity.EmailVerifiedAtUtc = user.EmailVerifiedAtUtc;
        entity.PendingEmailAddress = user.PendingEmailAddress;
        ApplyAvatar(entity, user.Avatar);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A null avatar means "caller did not express an avatar intent" (for example an admin updating
    /// roles), so the persisted selection is left untouched rather than silently reset.
    /// </summary>
    private static void ApplyAvatar(UserEntity entity, UserAvatar? avatar)
    {
        if (avatar is null)
            return;

        entity.AvatarKind = avatar.StorageKind;
        entity.AvatarOptionId = avatar.Kind == UserAvatarKind.BuiltIn ? avatar.BuiltInOptionId : null;
        entity.AvatarImageKey = avatar.Kind == UserAvatarKind.Custom ? avatar.ImageKey : null;
    }

    /// <inheritdoc />
    public async ValueTask UpdateLastAuthenticatedAtAsync(IdentitySource source, string externalId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (source == IdentitySource.Entra)
        {
            throw new ArgumentException(
                "Entra last-authenticated updates require tenant id and object id. Use ProviderIdentityKey.Entra(tenantId, objectId).",
                nameof(source));
        }

        await UpdateLastAuthenticatedAtCoreAsync(
            l => l.Source == (int)source && l.ExternalId == externalId,
            source,
            externalId,
            at,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask UpdateLastAuthenticatedAtAsync(ProviderIdentityKey identity, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (identity.Source == IdentitySource.Entra)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identity.TenantId);
            await UpdateLastAuthenticatedAtCoreAsync(
                l => l.Source == (int)IdentitySource.Entra
                    && l.TenantId == identity.TenantId
                    && l.ExternalId == identity.ExternalId,
                identity.Source,
                identity.ExternalId,
                at,
                cancellationToken);
            return;
        }

        await UpdateLastAuthenticatedAtAsync(identity.Source, identity.ExternalId, at, cancellationToken);
    }

    private async ValueTask UpdateLastAuthenticatedAtCoreAsync(
        System.Linq.Expressions.Expression<Func<IdentityLinkEntity, bool>> match,
        IdentitySource source,
        string externalId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        try
        {
            var writeOnlyIfOlderThan = at - LastAuthenticatedAtWriteInterval;

            await _db.IdentityLinks
                .Where(match)
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
        var query = _db.Users.AsNoTracking().Where(u => !u.IsBreakGlassEligible);
        var totalCount = await query.CountAsync(cancellationToken);
        var userEntities = await query
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
        if (source == IdentitySource.Entra)
        {
            throw new InvalidOperationException(
                "Entra identity links require tenant id. Use AddIdentityLinkAsync(UserId, IdentityLink).");
        }

        if (await IdentityExistsAsync(source, externalId, tenantId: null, cancellationToken))
            throw new InvalidOperationException("An identity link with this source and external id already exists.");

        await RequireLinkTargetAsync(userId, source, externalId, cancellationToken);

        _db.IdentityLinks.Add(new IdentityLinkEntity
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            UserId = userId.Value,
            Source = (int)source,
            ExternalId = externalId,
            LastAuthenticatedAt = null,
            LinkedAt = DateTimeOffset.UtcNow
        });
        await SaveIdentityChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask AddIdentityLinkAsync(UserId userId, IdentityLink link, CancellationToken cancellationToken = default)
    {
        if (await IdentityExistsAsync(link, cancellationToken))
            throw new InvalidOperationException("An identity link with this source and external id already exists.");

        await RequireLinkTargetAsync(userId, link.Source, link.ExternalId, cancellationToken);

        _db.IdentityLinks.Add(ToEntity(userId, link));
        await SaveIdentityChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The user must exist, and a Local link must use the user's username as its external id. The
    /// one-Local-link-per-user index rejects a second Local link at the database.
    /// </summary>
    private async ValueTask RequireLinkTargetAsync(
        UserId userId,
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken)
    {
        var username = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"User {userId.Value} not found.");

        if (source == IdentitySource.Local && !string.Equals(externalId, username, StringComparison.Ordinal))
        {
            throw new UsernameException(
                UsernameErrorCodes.Mismatch,
                "A Local identity link must use the user's username.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<IdentityLinkOwner?> FindIdentityLinkAsync(
        IdentitySource source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        if (source == IdentitySource.Entra)
        {
            throw new ArgumentException(
                "Entra identity lookup requires tenant id and object id. Use ProviderIdentityKey.Entra(tenantId, objectId).",
                nameof(source));
        }

        var link = await _db.IdentityLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Source == (int)source && l.ExternalId == externalId, cancellationToken);
        return link is null ? null : new IdentityLinkOwner(new UserId(link.UserId), ToDomain(link));
    }

    /// <inheritdoc />
    public async ValueTask<IdentityLinkOwner?> FindIdentityLinkAsync(
        ProviderIdentityKey identity,
        CancellationToken cancellationToken = default)
    {
        var link = await FindLinkEntityAsync(identity, tracking: false, cancellationToken);
        return link is null ? null : new IdentityLinkOwner(new UserId(link.UserId), ToDomain(link));
    }

    /// <inheritdoc />
    public async ValueTask CompleteEntraLinkProofAsync(
        string tenantId,
        string objectId,
        DateTimeOffset provenAt,
        string? registrationIdWhenMissing,
        string? providerSubject,
        CancellationToken cancellationToken = default)
    {
        var key = ProviderIdentityKey.Entra(tenantId, objectId);
        var link = await FindLinkEntityAsync(key, tracking: true, cancellationToken);
        if (link is null)
            return;

        link.LastAuthenticatedAt = provenAt;
        if (string.IsNullOrEmpty(link.RegistrationId) && !string.IsNullOrEmpty(registrationIdWhenMissing))
            link.RegistrationId = registrationIdWhenMissing;
        if (!string.IsNullOrEmpty(providerSubject))
            link.ProviderSubject = providerSubject;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogDebug(
                ex,
                "Ignored optimistic concurrency while completing Entra link proof for {TenantId}/{ExternalId}.",
                key.TenantId,
                key.ExternalId);
        }
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

    private async ValueTask<IdentityLinkEntity?> FindLinkEntityAsync(
        ProviderIdentityKey identity,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking ? _db.IdentityLinks : _db.IdentityLinks.AsNoTracking();
        if (identity.Source == IdentitySource.Entra)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identity.TenantId);
            return await query.FirstOrDefaultAsync(
                l => l.Source == (int)IdentitySource.Entra
                    && l.TenantId == identity.TenantId
                    && l.ExternalId == identity.ExternalId,
                cancellationToken);
        }

        return await query.FirstOrDefaultAsync(
            l => l.Source == (int)identity.Source && l.ExternalId == identity.ExternalId,
            cancellationToken);
    }

    private ValueTask<bool> IdentityExistsAsync(IdentityLink link, CancellationToken cancellationToken) =>
        IdentityExistsAsync(link.Source, link.ExternalId, link.TenantId, cancellationToken);

    private async ValueTask<bool> IdentityExistsAsync(
        IdentitySource source,
        string externalId,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        if (source == IdentitySource.Entra)
        {
            var key = ProviderIdentityKey.Entra(
                tenantId ?? throw new InvalidOperationException("Entra identity links require a tenant id."),
                externalId);
            return await _db.IdentityLinks.AnyAsync(
                l => l.Source == (int)IdentitySource.Entra
                    && l.TenantId == key.TenantId
                    && l.ExternalId == key.ExternalId,
                cancellationToken);
        }

        return await _db.IdentityLinks.AnyAsync(
            l => l.Source == (int)source && l.ExternalId == externalId,
            cancellationToken);
    }

    private async ValueTask SaveIdentityChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUsernameUniqueViolation(ex))
        {
            throw new UsernameException(
                UsernameErrorCodes.Taken,
                "That username belongs, or once belonged, to another user.");
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException("An identity link with this source and external id already exists.", ex);
        }
    }

    private static bool IsUniqueConstraintViolation(Exception ex) => FindUniqueViolation(ex) is not null;

    /// <summary>The ledger primary key or <c>UX_Users_Username</c>: the name is or was someone else's.</summary>
    private static bool IsUsernameUniqueViolation(Exception ex)
    {
        var message = FindUniqueViolation(ex)?.Message;
        return message is not null
            && (message.Contains("PK_UsernameLedger", StringComparison.Ordinal)
                || message.Contains("UX_Users_Username", StringComparison.Ordinal));
    }

    private static bool IsCurrentUsernameIndexViolation(Exception ex) =>
        FindUniqueViolation(ex)?.Message.Contains("UX_UsernameLedger_Current_UserId", StringComparison.Ordinal) == true;

    private static Microsoft.Data.SqlClient.SqlException? FindUniqueViolation(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.SqlClient.SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627))
            {
                return sql;
            }
        }

        return null;
    }

    private static IdentityLinkEntity ToEntity(UserId userId, IdentityLink link) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..24],
        UserId = userId.Value,
        Source = (int)link.Source,
        ExternalId = link.ExternalId,
        LastAuthenticatedAt = link.LastAuthenticatedAt,
        TenantId = link.TenantId,
        RegistrationId = link.RegistrationId,
        LinkedAt = link.LinkedAt ?? DateTimeOffset.UtcNow,
        ProviderSubject = link.ProviderSubject,
        DisplayNameSnapshot = link.DisplayNameSnapshot,
        UserPrincipalNameSnapshot = link.UserPrincipalNameSnapshot,
        MailSnapshot = link.MailSnapshot
    };

    private static IdentityLink ToDomain(IdentityLinkEntity l) => new(
        (IdentitySource)l.Source,
        l.ExternalId,
        l.LastAuthenticatedAt,
        l.TenantId,
        l.RegistrationId,
        l.LinkedAt,
        l.ProviderSubject,
        l.DisplayNameSnapshot,
        l.UserPrincipalNameSnapshot,
        l.MailSnapshot);

    private static User ToDomain(UserEntity userEntity, List<IdentityLinkEntity> links, List<string> roleIds)
    {
        var identityLinks = links.Select(ToDomain).ToList();
        var assignedRoleIds = roleIds.Select(r => new RoleId(r)).ToList();

        return new User(
            new UserId(userEntity.Id),
            userEntity.DisplayName,
            identityLinks,
            assignedRoleIds,
            userEntity.IsBreakGlassEligible,
            userEntity.DisplayTimeZoneId,
            UserAvatar.FromStorage(
                userEntity.AvatarKind,
                userEntity.AvatarOptionId,
                userEntity.AvatarImageKey),
            userEntity.EmailAddress,
            userEntity.EmailVerifiedAtUtc,
            userEntity.PendingEmailAddress)
        {
            Username = userEntity.Username
        };
    }

    /// <inheritdoc />
    public async ValueTask<User?> FindByVerifiedContactEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return null;

        var entity = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.EmailAddress == normalizedEmail && e.EmailVerifiedAtUtc != null,
                cancellationToken);
        return entity is null ? null : await GetByIdAsync(new UserId(entity.Id), cancellationToken);
    }
}
