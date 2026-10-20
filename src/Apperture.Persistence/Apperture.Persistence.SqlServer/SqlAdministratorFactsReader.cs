using Apperture.Core.Application;
using Apperture.Core.Application.AdministratorViability;
using Apperture.Core.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <inheritdoc cref="IAdministratorFactsReader" />
/// <remarks>
/// Set-based and read-only (<c>AsNoTracking</c>): the number of statements does not depend on the number of administrators, and
/// nothing here takes a lock. Each statement is its own read, so a caller that needs the statements to describe one state takes
/// <see cref="IPlatformAdministratorSetGate"/> first.
/// </remarks>
public sealed class SqlAdministratorFactsReader : IAdministratorFactsReader
{
    private const string AdministratorRoleId = BootstrapConstants.PlatformAdminRoleId;

    private readonly AppertureDbContext _db;

    public SqlAdministratorFactsReader(AppertureDbContext db) => _db = db;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AdministratorFacts>> ReadAdministratorsAsync(CancellationToken cancellationToken = default)
    {
        var holders = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.RoleId == AdministratorRoleId)
            .Join(_db.Users.AsNoTracking(), ur => ur.UserId, u => u.Id, (ur, u) => new { u.Id, u.IsBreakGlassEligible })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (holders.Count == 0)
            return [];

        var localLinkUsers = await (
                from l in _db.IdentityLinks.AsNoTracking()
                where l.Source == (int)IdentitySource.Local
                      && _db.UserRoles.Any(ur => ur.UserId == l.UserId && ur.RoleId == AdministratorRoleId)
                select l.UserId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var enabledCredentialUsers = await (
                from c in _db.LocalCredentials.AsNoTracking()
                where !c.IsDisabled
                      && _db.UserRoles.Any(ur => ur.UserId == c.UserId && ur.RoleId == AdministratorRoleId)
                select c.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var provenEntraLinks = await (
                from l in _db.IdentityLinks.AsNoTracking()
                where l.Source == (int)IdentitySource.Entra
                      && l.LastAuthenticatedAt != null
                      && _db.UserRoles.Any(ur => ur.UserId == l.UserId && ur.RoleId == AdministratorRoleId)
                select new { l.UserId, l.TenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var localLinks = localLinkUsers.ToHashSet(StringComparer.Ordinal);
        var enabledCredentials = enabledCredentialUsers.ToHashSet(StringComparer.Ordinal);
        var entraTenants = provenEntraLinks
            .GroupBy(l => l.UserId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string?>)g.Select(l => l.TenantId).ToList(), StringComparer.Ordinal);

        return holders
            .OrderBy(h => h.Id, StringComparer.Ordinal)
            .Select(h => new AdministratorFacts(
                h.Id,
                h.IsBreakGlassEligible,
                localLinks.Contains(h.Id),
                enabledCredentials.Contains(h.Id),
                entraTenants.TryGetValue(h.Id, out var tenants) ? tenants : []))
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<RecoveryAccessFacts> ReadRecoveryAccessAsync(CancellationToken cancellationToken = default)
    {
        // The designation flag is a single-row fact; ordering by id keeps the answer stable if data ever carried two.
        var designated = await _db.Users.AsNoTracking()
            .Where(u => u.IsBreakGlassEligible)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (designated is null)
            return new RecoveryAccessFacts(null, false, false, false, false, 0);

        var holdsRole = await _db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == designated && ur.RoleId == AdministratorRoleId, cancellationToken)
            .ConfigureAwait(false);
        var hasLocalLink = await _db.IdentityLinks.AsNoTracking()
            .AnyAsync(l => l.UserId == designated && l.Source == (int)IdentitySource.Local, cancellationToken)
            .ConfigureAwait(false);
        var credentialEnabled = await _db.LocalCredentials.AsNoTracking()
            .AnyAsync(c => c.UserId == designated && !c.IsDisabled, cancellationToken)
            .ConfigureAwait(false);
        var mfaEnrolled = await _db.UserTotpCredentials.AsNoTracking()
            .AnyAsync(t => t.UserId == designated && t.IsEnrolled, cancellationToken)
            .ConfigureAwait(false);
        var unusedCodes = mfaEnrolled
            ? await _db.UserMfaRecoveryCodes.AsNoTracking()
                .CountAsync(c => c.UserId == designated && c.ConsumedAtUtc == null, cancellationToken)
                .ConfigureAwait(false)
            : 0;

        return new RecoveryAccessFacts(designated, holdsRole, hasLocalLink, credentialEnabled, mfaEnrolled, unusedCodes);
    }
}
