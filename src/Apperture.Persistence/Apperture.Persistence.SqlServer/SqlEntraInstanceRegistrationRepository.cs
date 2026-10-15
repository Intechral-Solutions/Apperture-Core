using Apperture.Core.Application.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Apperture.Persistence.SqlServer;

/// <summary>
/// The Entra registrations. A registration's configuration lives in <c>EntraInstanceRegistrations</c>; the outcome of its latest
/// OpenID validation and directory discovery lives in <c>EntraRegistrationDiagnostics</c> (SEC-2.4). The two probe methods below
/// are operational telemetry and write the diagnostics table only: they never update the registration row, so they leave its
/// <c>UpdatedUtc</c> and <c>rowversion</c> alone and cannot make an administrator's save fail. A read joins the two, so a
/// registration still carries the diagnostics it always did.
/// </summary>
public sealed class SqlEntraInstanceRegistrationRepository : IEntraInstanceRegistrationRepository
{
    private const int SqlDuplicateKey = 2627;
    private const int SqlDuplicateUniqueIndex = 2601;
    private const int SqlForeignKeyViolation = 547;

    private readonly AppertureDbContext _db;

    public SqlEntraInstanceRegistrationRepository(AppertureDbContext db) => _db = db;

    private IQueryable<EntraInstanceRegistrationEntity> ReadModel() =>
        _db.EntraInstanceRegistrations.AsNoTracking().Include(x => x.Diagnostics);

    public async Task<EntraInstanceRegistration?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var e = await ReadModel()
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async Task<EntraInstanceRegistration?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var e = await ReadModel()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return e is null ? null : ToDomain(e);
    }

    public async Task<IReadOnlyList<EntraInstanceRegistration>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = await ReadModel()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return list.ConvertAll(ToDomain);
    }

    public async Task<EntraInstanceRegistration> AddAsync(EntraInstanceRegistration registration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var openIdStatus = registration.OpenIdConfigurationStatus == OpenIdConfigurationStatus.Unknown
            ? OpenIdConfigurationStatus.NotValidated
            : registration.OpenIdConfigurationStatus;
        var dirStatus = registration.DirectoryDiscoveryStatus == DirectoryDiscoveryStatus.Unknown
            ? DirectoryDiscoveryStatus.NotAttempted
            : registration.DirectoryDiscoveryStatus;

        var e = new EntraInstanceRegistrationEntity
        {
            Id = string.IsNullOrWhiteSpace(registration.Id) ? Guid.NewGuid().ToString("N") : registration.Id,
            DisplayName = registration.DisplayName,
            Authority = registration.Authority,
            ClientId = registration.ClientId,
            UsesDeploymentSecret = registration.UsesDeploymentSecret,
            ClientSecretRef = registration.ClientSecretRef,
            CallbackPath = registration.CallbackPath,
            Enabled = registration.Enabled,
            IsActive = registration.IsActive,
            CreatedUtc = now,
            UpdatedUtc = now,
            AzureTenantId = registration.AzureTenantId,
            AuthenticationContextId = registration.AuthenticationContextId,
            AllowMemberAccessRequests = registration.AllowMemberAccessRequests,
            AllowGuestAccessRequests = registration.AllowGuestAccessRequests,
            // The diagnostics row is created with the registration, in the same save, so every registration has one.
            Diagnostics = new EntraRegistrationDiagnosticsEntity
            {
                OpenIdConfigurationStatus = (int)openIdStatus,
                LastOpenIdValidationUtc = registration.LastOpenIdValidationUtc,
                LastOpenIdValidationSummary = registration.LastOpenIdValidationSummary,
                LastOpenIdValidationDetail = registration.LastOpenIdValidationDetail,
                DirectoryDiscoveryStatus = (int)dirStatus,
                LastDirectoryDiscoveryUtc = registration.LastDirectoryDiscoveryUtc,
                LastDirectoryDiscoverySummary = registration.LastDirectoryDiscoverySummary,
                LastDirectoryDiscoveryDetail = registration.LastDirectoryDiscoveryDetail
            }
        };
        _db.EntraInstanceRegistrations.Add(e);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDomain(e);
    }

    public async Task UpdateAsync(EntraInstanceRegistration registration, byte[]? expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations
            .FirstOrDefaultAsync(x => x.Id == registration.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Entra registration '{registration.Id}' was not found.");

        if (expectedRowVersion is not null && (e.RowVersion is null || !e.RowVersion.SequenceEqual(expectedRowVersion)))
            throw new DbUpdateConcurrencyException("Entra registration was modified by another request.");

        e.DisplayName = registration.DisplayName;
        e.Authority = registration.Authority;
        e.ClientId = registration.ClientId;
        e.UsesDeploymentSecret = registration.UsesDeploymentSecret;
        e.ClientSecretRef = registration.ClientSecretRef;
        e.CallbackPath = registration.CallbackPath;
        e.AzureTenantId = string.IsNullOrWhiteSpace(registration.AzureTenantId)
            ? null
            : registration.AzureTenantId.Trim();
        e.AuthenticationContextId = string.IsNullOrWhiteSpace(registration.AuthenticationContextId)
            ? null
            : registration.AuthenticationContextId.Trim();
        e.Enabled = registration.Enabled;
        e.AllowMemberAccessRequests = registration.AllowMemberAccessRequests;
        e.AllowGuestAccessRequests = registration.AllowGuestAccessRequests;
        if (!registration.Enabled && e.IsActive)
            e.IsActive = false;
        e.UpdatedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var e = await _db.EntraInstanceRegistrations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
            return;
        _db.EntraInstanceRegistrations.Remove(e);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveAsync(string id, CancellationToken cancellationToken = default)
    {
        // AUD-1.2 (gate §13.4): inside an audited unit of work the switch joins the unit's transaction, so it commits with its
        // audit row and the unit owns the commit, verification and any rerun.
        if (_db.Database.CurrentTransaction is not null)
        {
            await SwitchActiveAsync(id, cancellationToken);
            return;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            await SwitchActiveAsync(id, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }

    private async Task SwitchActiveAsync(string id, CancellationToken cancellationToken)
    {
        var rows = await _db.EntraInstanceRegistrations.ToListAsync(cancellationToken);
        var target = rows.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException($"Entra registration '{id}' was not found.");
        if (!target.Enabled)
            throw new InvalidOperationException("Cannot activate a disabled registration.");

        var now = DateTimeOffset.UtcNow;
        foreach (var r in rows)
        {
            r.IsActive = r.Id == id;
            r.UpdatedUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    // Operational telemetry (SEC-2.4, gate P-7, P-8, P-10): no audit row, and no write to the registration table. Each outcome is
    // one set-based statement on the diagnostics row alone, so a probe is not a read-modify-write that could overwrite the other
    // probe's columns, and the last probe to finish is the one recorded. The common path does not even read the registration; the
    // fallback that creates a missing diagnostics row reads it (for its id) and writes nothing to it.

    public async Task UpdateOpenIdValidationOutcomeAsync(
        string id,
        OpenIdConfigurationStatus status,
        DateTimeOffset validatedUtc,
        string? summary,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        var statusValue = (int)status;
        await RecordAsync(
            update: () => _db.EntraRegistrationDiagnostics
                .Where(d => d.RegistrationId == id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(d => d.OpenIdConfigurationStatus, statusValue)
                        .SetProperty(d => d.LastOpenIdValidationUtc, validatedUtc)
                        .SetProperty(d => d.LastOpenIdValidationSummary, summary)
                        .SetProperty(d => d.LastOpenIdValidationDetail, detail),
                    cancellationToken),
            // The row is missing only for a registration that was inserted without one. The other group keeps its defaults.
            insert: () => _db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO [EntraRegistrationDiagnostics]
                    ([RegistrationId], [OpenIdConfigurationStatus], [LastOpenIdValidationUtc], [LastOpenIdValidationSummary], [LastOpenIdValidationDetail])
                SELECT r.[Id], @status, @utc, @summary, @detail
                FROM [EntraInstanceRegistrations] AS r
                WHERE r.[Id] = @id
                  AND NOT EXISTS (SELECT 1 FROM [EntraRegistrationDiagnostics] AS d WHERE d.[RegistrationId] = r.[Id]);
                """,
                [
                    new SqlParameter("@id", System.Data.SqlDbType.NVarChar, 64) { Value = id },
                    new SqlParameter("@status", System.Data.SqlDbType.Int) { Value = statusValue },
                    new SqlParameter("@utc", System.Data.SqlDbType.DateTimeOffset) { Value = validatedUtc },
                    new SqlParameter("@summary", System.Data.SqlDbType.NVarChar, 512) { Value = (object?)summary ?? DBNull.Value },
                    new SqlParameter("@detail", System.Data.SqlDbType.NVarChar, 4000) { Value = (object?)detail ?? DBNull.Value }
                ],
                cancellationToken));
    }

    public async Task UpdateDirectoryDiscoveryOutcomeAsync(
        string id,
        DirectoryDiscoveryStatus status,
        DateTimeOffset utc,
        string? summary,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        var statusValue = (int)status;
        await RecordAsync(
            update: () => _db.EntraRegistrationDiagnostics
                .Where(d => d.RegistrationId == id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(d => d.DirectoryDiscoveryStatus, statusValue)
                        .SetProperty(d => d.LastDirectoryDiscoveryUtc, utc)
                        .SetProperty(d => d.LastDirectoryDiscoverySummary, summary)
                        .SetProperty(d => d.LastDirectoryDiscoveryDetail, detail),
                    cancellationToken),
            insert: () => _db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO [EntraRegistrationDiagnostics]
                    ([RegistrationId], [DirectoryDiscoveryStatus], [LastDirectoryDiscoveryUtc], [LastDirectoryDiscoverySummary], [LastDirectoryDiscoveryDetail])
                SELECT r.[Id], @status, @utc, @summary, @detail
                FROM [EntraInstanceRegistrations] AS r
                WHERE r.[Id] = @id
                  AND NOT EXISTS (SELECT 1 FROM [EntraRegistrationDiagnostics] AS d WHERE d.[RegistrationId] = r.[Id]);
                """,
                [
                    new SqlParameter("@id", System.Data.SqlDbType.NVarChar, 64) { Value = id },
                    new SqlParameter("@status", System.Data.SqlDbType.Int) { Value = statusValue },
                    new SqlParameter("@utc", System.Data.SqlDbType.DateTimeOffset) { Value = utc },
                    new SqlParameter("@summary", System.Data.SqlDbType.NVarChar, 512) { Value = (object?)summary ?? DBNull.Value },
                    new SqlParameter("@detail", System.Data.SqlDbType.NVarChar, 4000) { Value = (object?)detail ?? DBNull.Value }
                ],
                cancellationToken));
    }

    /// <summary>
    /// Records one probe outcome on the registration's diagnostics row, creating the row when it is missing. A registration that
    /// does not exist (or was deleted meanwhile) records nothing, as before. The diagnostics row is the only thing written.
    /// </summary>
    private static async Task RecordAsync(Func<Task<int>> update, Func<Task<int>> insert)
    {
        if (await update() > 0)
            return;

        try
        {
            if (await insert() > 0)
                return;
        }
        catch (Exception ex) when (IsConstraintRace(ex))
        {
            // A concurrent probe created the row first (duplicate key), or the registration was deleted between the check and
            // the insert (foreign key). Either way the retry below records the outcome on the row that now exists, or nothing.
        }

        // The insert wrote nothing: either the registration is gone, or a concurrent probe created the row first. Record on
        // whichever row exists now; for a registration that is gone this updates no row.
        await update();
    }

    private static bool IsConstraintRace(Exception exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException { Number: SqlDuplicateKey or SqlDuplicateUniqueIndex or SqlForeignKeyViolation })
                return true;
        }

        return false;
    }

    private static EntraInstanceRegistration ToDomain(EntraInstanceRegistrationEntity e)
    {
        // A registration with no diagnostics row has never been probed: the defaults the column always carried.
        var d = e.Diagnostics;
        return new()
        {
            Id = e.Id,
            DisplayName = e.DisplayName,
            Authority = e.Authority,
            ClientId = e.ClientId,
            UsesDeploymentSecret = e.UsesDeploymentSecret,
            ClientSecretRef = e.ClientSecretRef,
            CallbackPath = e.CallbackPath,
            Enabled = e.Enabled,
            IsActive = e.IsActive,
            RowVersion = e.RowVersion is { Length: > 0 } ? e.RowVersion.ToArray() : null,
            CreatedUtc = e.CreatedUtc,
            UpdatedUtc = e.UpdatedUtc,
            AzureTenantId = e.AzureTenantId,
            AuthenticationContextId = e.AuthenticationContextId,
            OpenIdConfigurationStatus = (OpenIdConfigurationStatus)(d?.OpenIdConfigurationStatus ?? EntraRegistrationDiagnosticsEntity.DefaultOpenIdConfigurationStatus),
            LastOpenIdValidationUtc = d?.LastOpenIdValidationUtc,
            LastOpenIdValidationSummary = d?.LastOpenIdValidationSummary,
            LastOpenIdValidationDetail = d?.LastOpenIdValidationDetail,
            DirectoryDiscoveryStatus = (DirectoryDiscoveryStatus)(d?.DirectoryDiscoveryStatus ?? EntraRegistrationDiagnosticsEntity.DefaultDirectoryDiscoveryStatus),
            LastDirectoryDiscoveryUtc = d?.LastDirectoryDiscoveryUtc,
            LastDirectoryDiscoverySummary = d?.LastDirectoryDiscoverySummary,
            LastDirectoryDiscoveryDetail = d?.LastDirectoryDiscoveryDetail,
            AllowMemberAccessRequests = e.AllowMemberAccessRequests,
            AllowGuestAccessRequests = e.AllowGuestAccessRequests
        };
    }
}
