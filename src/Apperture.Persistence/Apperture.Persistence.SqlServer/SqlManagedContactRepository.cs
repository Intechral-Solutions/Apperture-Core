using Apperture.Core.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

public sealed class SqlManagedContactRepository : IManagedContactRepository
{
    private readonly AppertureDbContext _db;

    public SqlManagedContactRepository(AppertureDbContext db) => _db = db;

    public async Task<ManagedContactRepositoryRow?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var trimmed = id.Trim();
        var row = await _db.ManagedContacts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == trimmed, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<ManagedContactRepositoryRow?> FindByEmailAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
            return null;
        var norm = NormalizeEmail(emailAddress);
        var row = await _db.ManagedContacts.AsNoTracking()
            .Where(x => x.EmailAddress != null)
            .FirstOrDefaultAsync(
                x => x.EmailAddress!.Trim().ToLower() == norm,
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<ManagedContactRepositoryRow>> SearchAsync(
        string? query,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var q = _db.ManagedContacts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var pattern = "%" + EscapeLike(term) + "%";
            q = q.Where(x =>
                EF.Functions.Like(x.DisplayName, pattern)
                || (x.EmailAddress != null && EF.Functions.Like(x.EmailAddress, pattern)));
        }

        var rows = await q
            .OrderBy(x => x.DisplayName)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<string> CreateCustomAsync(
        string displayName,
        string? emailAddress,
        bool isEnabled,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var id = NewId("mcontact_");
        var entity = new ManagedContactEntity
        {
            Id = id,
            DisplayName = displayName.Trim(),
            EmailAddress = string.IsNullOrWhiteSpace(emailAddress) ? null : emailAddress.Trim(),
            Source = ManagedContactSources.Custom,
            IsEnabled = isEnabled,
            LinkedUserId = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        _db.ManagedContacts.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateContactAsync(
        string id,
        string displayName,
        string? emailAddress,
        bool isEnabled,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var trimmed = id.Trim();
        var entity = await _db.ManagedContacts
            .FirstOrDefaultAsync(x => x.Id == trimmed, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            throw new InvalidOperationException("Managed contact not found.");
        if (!string.Equals(entity.Source, ManagedContactSources.Custom, StringComparison.Ordinal))
            throw new InvalidOperationException("Only custom contacts can be edited through this path.");
        entity.DisplayName = displayName.Trim();
        entity.EmailAddress = string.IsNullOrWhiteSpace(emailAddress) ? null : emailAddress.Trim();
        entity.IsEnabled = isEnabled;
        entity.UpdatedAtUtc = nowUtc;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLinkedUserIdAsync(
        string managedContactId,
        string? userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ManagedContacts
            .FirstOrDefaultAsync(x => x.Id == managedContactId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            throw new InvalidOperationException("Managed contact not found.");
        entity.LinkedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        entity.UpdatedAtUtc = nowUtc;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetIsEnabledAsync(
        string id,
        bool isEnabled,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ManagedContacts
            .FirstOrDefaultAsync(x => x.Id == id.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            throw new InvalidOperationException("Managed contact not found.");
        entity.IsEnabled = isEnabled;
        entity.UpdatedAtUtc = nowUtc;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UpsertEntraManagedContactResult> UpsertFromEntraDirectoryUserAsync(
        string displayName,
        string? mail,
        string? userPrincipalName,
        string entraObjectId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var oid = entraObjectId.Trim();
        var linkRow = await _db.ManagedContactIdentityLinks
            .FirstOrDefaultAsync(
                x => x.Provider == ManagedContactIdentityProviders.Entra && x.ExternalObjectId == oid,
                cancellationToken)
            .ConfigureAwait(false);

        var dn = string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(userPrincipalName) ? oid : userPrincipalName.Trim())
            : displayName.Trim();
        var email = string.IsNullOrWhiteSpace(mail) ? null : mail.Trim();

        if (linkRow is not null)
        {
            var contact = await _db.ManagedContacts
                .FirstAsync(x => x.Id == linkRow.ManagedContactId, cancellationToken)
                .ConfigureAwait(false);
            contact.DisplayName = dn;
            contact.EmailAddress = email;
            contact.Source = ManagedContactSources.Entra;
            contact.UpdatedAtUtc = nowUtc;
            linkRow.ExternalPrincipalName = string.IsNullOrWhiteSpace(userPrincipalName) ? null : userPrincipalName.Trim();
            linkRow.LastSyncedAtUtc = nowUtc;
            linkRow.LinkStatus = ManagedContactIdentityLinkStatuses.Active;
            linkRow.UpdatedAtUtc = nowUtc;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var linkDto = MapLink(await _db.ManagedContactIdentityLinks.AsNoTracking()
                .FirstAsync(x => x.Id == linkRow.Id, cancellationToken)
                .ConfigureAwait(false));
            return new UpsertEntraManagedContactResult(contact.Id, Created: false, linkDto);
        }

        var contactId = NewId("mcontact_");
        var contactEntity = new ManagedContactEntity
        {
            Id = contactId,
            DisplayName = dn,
            EmailAddress = email,
            Source = ManagedContactSources.Entra,
            IsEnabled = true,
            LinkedUserId = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        var linkEntity = new ManagedContactIdentityLinkEntity
        {
            Id = NewId("mctlnk_"),
            ManagedContactId = contactId,
            Provider = ManagedContactIdentityProviders.Entra,
            ExternalObjectId = oid,
            ExternalPrincipalName = string.IsNullOrWhiteSpace(userPrincipalName) ? null : userPrincipalName.Trim(),
            ExternalGroupObjectId = null,
            LastSyncedAtUtc = nowUtc,
            LinkStatus = ManagedContactIdentityLinkStatuses.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        _db.ManagedContacts.Add(contactEntity);
        _db.ManagedContactIdentityLinks.Add(linkEntity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var linkDto2 = MapLink(linkEntity);
        return new UpsertEntraManagedContactResult(contactId, Created: true, linkDto2);
    }

    public async Task<ManagedContactIdentityLinkRepositoryRow?> FindIdentityLinkByEntraObjectIdAsync(
        string entraObjectId,
        CancellationToken cancellationToken = default)
    {
        var oid = entraObjectId.Trim();
        var link = await _db.ManagedContactIdentityLinks.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Provider == ManagedContactIdentityProviders.Entra && x.ExternalObjectId == oid,
                cancellationToken)
            .ConfigureAwait(false);
        return link is null ? null : MapLink(link);
    }

    public async Task<IReadOnlyList<ManagedContactIdentityLinkRepositoryRow>> ListLinksForContactAsync(
        string managedContactId,
        CancellationToken cancellationToken = default)
    {
        var id = managedContactId.Trim();
        var links = await _db.ManagedContactIdentityLinks.AsNoTracking()
            .Where(x => x.ManagedContactId == id)
            .OrderBy(x => x.Provider)
            .ThenBy(x => x.ExternalObjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return links.Select(MapLink).ToList();
    }

    private static ManagedContactRepositoryRow Map(ManagedContactEntity x) =>
        new(x.Id, x.DisplayName, x.EmailAddress, x.Source, x.IsEnabled, x.LinkedUserId, x.CreatedAtUtc, x.UpdatedAtUtc);

    private static ManagedContactIdentityLinkRepositoryRow MapLink(ManagedContactIdentityLinkEntity x) =>
        new(x.Id, x.ManagedContactId, x.Provider, x.ExternalObjectId, x.ExternalPrincipalName, x.LastSyncedAtUtc, x.LinkStatus, x.CreatedAtUtc, x.UpdatedAtUtc);

    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N");

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string EscapeLike(string value) => value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
