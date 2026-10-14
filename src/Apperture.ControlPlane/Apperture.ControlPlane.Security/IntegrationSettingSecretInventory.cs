using Apperture.Core.Application.PlatformSettings;
using Apperture.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Read-only database inventory for an integration delete (AUD-1.4 slice 3, work package 4a): the persisted setting overrides an
/// integration owns, and the references to a candidate secret that would survive the delete.
/// </summary>
/// <remarks>
/// <para><b>Overrides.</b> Found by ownership alone: every <c>PlatformSettingValues</c> row whose scope is this integration. No
/// setting definition, provider or schema entry is consulted, so a key whose definition was removed, reclassified or never
/// registered is still listed. Which rows to remove is a different question from which secrets to retire; this answers only the
/// first.</para>
/// <para><b>Surviving references.</b> The known locations in the database are every <c>PlatformSettingValues</c> row outside this
/// integration (all other scopes, other integrations included), <c>Connections.SecretRefsJson</c> (a JSON object of field key to
/// reference) and <c>EntraInstanceRegistrations.ClientSecretRef</c>. Each is read in full, with the columns projected, and compared
/// by GUID value through <see cref="SecretReferenceReader"/>: the scan is not narrowed by text, so no format or case variant of a
/// reference can be missed by a prefilter. The scale this relies on is configuration data: a handful of connections and
/// registrations and the settings table, each value a few hundred characters at most.</para>
/// <para><b>Read-only.</b> Every query is untracked, nothing is saved, and no entity on the shared context is touched. It joins the
/// caller's transaction when there is one and opens none.</para>
/// <para><b>What it does not know, and what a scan cannot promise.</b> It knows the three locations above and no others: a secret
/// reference held in configuration files, an external system, or any table or column added later is invisible to it. Nor is the scan
/// stable. Locking READ COMMITTED releases its read locks as it goes, and READ_COMMITTED_SNAPSHOT reads the last committed version
/// and ignores writers in flight, so under either a reference written by a transaction that commits after the scan is not seen.
/// The caller's integration row lock serializes against the integration's own settings, notification and routing writers
/// (slice 3, work package 3) and not against connection or Entra registration writers, which take no such lock. A connection
/// create or update no longer accepts an operator-supplied reference (a field is given a value, never a reference, and a
/// reference is accepted only when it names a secret that field owns), so a connection cannot be made to name one of these
/// settings' secrets through the API between the scan and the commit; a row written outside the API, before that rule, can
/// still hold such a reference, which is why the scan reads the connection table at all. The approved policy accepts that
/// limit; it is not eliminated here.</para>
/// </remarks>
public sealed class IntegrationSettingSecretInventory
{
    private readonly AppertureDbContext _db;

    public IntegrationSettingSecretInventory(AppertureDbContext db) =>
        _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <summary>Every persisted integration-scoped override of <paramref name="integrationId"/>, in key order, whatever its definition says.</summary>
    public async Task<IReadOnlyList<IntegrationSettingOverride>> ListOverridesAsync(
        string integrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationId);

        // The same predicate the delete removes rows by. No definition lookup, no key allow-list.
        var rows = await _db.PlatformSettingValues
            .AsNoTracking()
            .Where(v => v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind && v.ScopeId == integrationId)
            .Select(v => new { v.Id, v.Key, v.ValueJson })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => new IntegrationSettingOverride(r.Id, r.Key, r.ValueJson))
            .ToList();
    }

    /// <summary>
    /// The references to each of <paramref name="candidates"/> that would survive deleting <paramref name="deletingIntegrationId"/>:
    /// every location above except that integration's own overrides, which the delete removes. A candidate nothing refers to has
    /// no entry. References held more than once are returned once per holder.
    /// </summary>
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<SurvivingSecretReference>>> FindSurvivingReferencesAsync(
        string deletingIntegrationId,
        IReadOnlyCollection<Guid> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deletingIntegrationId);
        ArgumentNullException.ThrowIfNull(candidates);

        // Every setting row outside the deleting integration, in every scope. The negation of ListOverridesAsync's predicate.
        return ScanAsync(
            v => !(v.ScopeKind == PlatformSettingScopeConstants.IntegrationKind && v.ScopeId == deletingIntegrationId),
            candidates,
            cancellationToken);
    }

    /// <summary>
    /// Every reference to each of <paramref name="candidates"/> in the known locations, as the caller's transaction sees them. Used
    /// after a setting row has released a reference (cleared, reset or repointed in that same transaction), so the row no longer
    /// counts and every other holder, including the same integration's other overrides, does.
    /// </summary>
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<SurvivingSecretReference>>> FindReferencesAsync(
        IReadOnlyCollection<Guid> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return ScanAsync(_ => true, candidates, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<SurvivingSecretReference>>> ScanAsync(
        System.Linq.Expressions.Expression<Func<PlatformSettingValueEntity, bool>> includeSettingRow,
        IReadOnlyCollection<Guid> candidates,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, List<SurvivingSecretReference>>();
        if (candidates.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<SurvivingSecretReference>>();

        var wanted = candidates.ToHashSet();

        void Note(SecretReferenceReading reading, SurvivingSecretReferenceKind kind, string ownerId)
        {
            foreach (var guid in reading.Guids)
            {
                if (!wanted.Contains(guid))
                    continue;
                if (!found.TryGetValue(guid, out var list))
                    found[guid] = list = [];
                if (!list.Any(r => r.Kind == kind && string.Equals(r.OwnerId, ownerId, StringComparison.Ordinal)))
                    list.Add(new SurvivingSecretReference(kind, ownerId));
            }
        }

        var settings = await _db.PlatformSettingValues
            .AsNoTracking()
            .Where(includeSettingRow)
            .Select(v => new { v.Id, v.ScopeKind, v.ValueJson })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in settings)
        {
            var kind = string.Equals(row.ScopeKind, PlatformSettingScopeConstants.IntegrationKind, StringComparison.OrdinalIgnoreCase)
                ? SurvivingSecretReferenceKind.OtherIntegrationSetting
                : SurvivingSecretReferenceKind.PlatformSetting;
            Note(SecretReferenceReader.ReadJson(row.ValueJson), kind, row.Id);
        }

        var connections = await _db.Connections
            .AsNoTracking()
            .Select(c => new { c.Id, c.SecretRefsJson })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var connection in connections)
            Note(SecretReferenceReader.ReadJson(connection.SecretRefsJson), SurvivingSecretReferenceKind.Connection, connection.Id);

        var registrations = await _db.EntraInstanceRegistrations
            .AsNoTracking()
            .Select(r => new { r.Id, r.ClientSecretRef })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var registration in registrations)
            Note(SecretReferenceReader.ReadText(registration.ClientSecretRef), SurvivingSecretReferenceKind.EntraRegistration, registration.Id);

        return found.ToDictionary(p => p.Key, p => (IReadOnlyList<SurvivingSecretReference>)p.Value);
    }
}
